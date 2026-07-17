import { useCallback, useEffect, useState } from 'react';
import './App.css';
import { DailyMenusSection } from './components/sections/DailyMenusSection';
import { HeatmapsSection } from './components/sections/HeatmapsSection';
import { MealOccurrencesSection } from './components/sections/MealOccurrencesSection';
import { MealsSection } from './components/sections/MealsSection';
import { PredictionsSection } from './components/sections/PredictionsSection';
import { SummarySection } from './components/sections/SummarySection';
import type { DashboardPage, DashboardPageOption, MealAnalyticsResponse } from './types';

const transientStatusCodes = new Set([502, 503, 504]);
const maxStartupRetries = 6;
const startupRetryDelayMs = 750;
const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? '';
const normalizedApiBaseUrl = configuredApiBaseUrl.replace(/\/+$/, '');
const mealsApiUrl = normalizedApiBaseUrl === '' ? '/api/meals' : `${normalizedApiBaseUrl}/api/meals`;
const themeStorageKey = 'avi-meals-theme';

type ThemeMode = 'light' | 'dark';

function getPreferredTheme(): ThemeMode {
	try {
		const stored = window.localStorage.getItem(themeStorageKey);
		if (stored === 'light' || stored === 'dark') {
			return stored;
		}
	} catch {
		// Ignore storage access errors (private mode, etc.).
	}

	try {
		if (typeof window.matchMedia === 'function'
			&& window.matchMedia('(prefers-color-scheme: dark)').matches) {
			return 'dark';
		}
	} catch {
		// Ignore matchMedia errors in limited environments.
	}

	return 'light';
}

function applyTheme(theme: ThemeMode): void {
	document.documentElement.setAttribute('data-theme', theme);
}

function delayAsync(delayMs: number, signal?: AbortSignal): Promise<void> {
	return new Promise((resolve, reject) => {
		const timeoutId = window.setTimeout(resolve, delayMs);

		if (signal === undefined) {
			return;
		}

		const onAbort = () => {
			window.clearTimeout(timeoutId);
			signal.removeEventListener('abort', onAbort);
			reject(new DOMException('The operation was aborted.', 'AbortError'));
		};

		signal.addEventListener('abort', onAbort, { once: true });
	});
}

const pageOptions: DashboardPageOption[] = [
	{ key: 'all', label: 'All' },
	{ key: 'summary', label: 'Summary' },
	{ key: 'predictions', label: 'Predictions' },
	{ key: 'heatmaps', label: 'Heatmaps' },
	{ key: 'meals', label: 'Catering catalog' },
	{ key: 'dailyMenus', label: 'Daily menus' },
	{ key: 'mealOccurrences', label: 'Meal occurrences' }
];

function App() {
	const [analytics, setAnalytics] = useState<MealAnalyticsResponse | null>(null);
	const [error, setError] = useState<string | null>(null);
	const [isLoading, setIsLoading] = useState(true);
	const [page, setPage] = useState<DashboardPage>('all');
	const [theme, setTheme] = useState<ThemeMode>(() => {
		if (typeof window === 'undefined') {
			return 'light';
		}

		const preferred = getPreferredTheme();
		applyTheme(preferred);
		return preferred;
	});

	const loadAnalytics = useCallback(async (signal?: AbortSignal) => {
		setIsLoading(true);
		setError(null);

		try {
			for (let attempt = 0; attempt <= maxStartupRetries; attempt++) {
				const response = await fetch(mealsApiUrl, { signal, cache: 'no-store' });
				if (response.ok) {
					const data = (await response.json()) as MealAnalyticsResponse;
					setAnalytics(data);
					return;
				}

				if (transientStatusCodes.has(response.status) && attempt < maxStartupRetries) {
					await delayAsync(startupRetryDelayMs, signal);
					continue;
				}

				throw new Error(`Request failed with status ${response.status}.`);
			}
		} catch (loadError) {
			if (signal?.aborted) {
				return;
			}

			const message = loadError instanceof Error
				? loadError.message
				: 'Unknown error while loading meals.';
			setError(message);
		} finally {
			if (!signal?.aborted) {
				setIsLoading(false);
			}
		}
	}, []);

	useEffect(() => {
		const controller = new AbortController();
		void loadAnalytics(controller.signal);
		return () => controller.abort();
	}, [loadAnalytics]);

	useEffect(() => {
		applyTheme(theme);
		try {
			window.localStorage.setItem(themeStorageKey, theme);
		} catch {
			// Ignore storage access errors (private mode, etc.).
		}
	}, [theme]);

	const toggleTheme = () => {
		setTheme(current => (current === 'light' ? 'dark' : 'light'));
	};

	const showPage = (target: DashboardPage): boolean => page === 'all' || page === target;

	return (
		<main className="app-shell" aria-busy={isLoading}>
			<header className="hero">
				<div className="brand-lockup">
					<img src={`${import.meta.env.BASE_URL}favicon.svg`} alt="" className="brand-mark" />
					<div>
						<p className="brand-kicker">Taylor University · Hodson Dining</p>
						<h1>AVI Meals</h1>
					</div>
				</div>
				<p className="hero-lede">
					Campus dining analytics for Taylor — Hodson residential menus, heatmaps, and predictions built from the public AVI Dish history.
				</p>
				<div className="toolbar">
					<button type="button" onClick={() => void loadAnalytics()} disabled={isLoading} className="button button-primary">
						{isLoading ? 'Loading...' : 'Refresh meals'}
					</button>
					<button
						type="button"
						onClick={toggleTheme}
						className="button button-theme"
						aria-pressed={theme === 'dark'}
						aria-label={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
					>
						{theme === 'dark' ? 'Light mode' : 'Dark mode'}
					</button>
				</div>
			</header>

			<nav className="page-nav" aria-label="Dashboard pages">
				<p className="page-nav-label" id="page-nav-label">Pages</p>
				<div className="page-nav-buttons" role="group" aria-labelledby="page-nav-label">
					{pageOptions.map(option => {
						const isCurrent = page === option.key;
						return (
							<button
								key={option.key}
								type="button"
								onClick={() => setPage(option.key)}
								aria-current={isCurrent ? 'page' : undefined}
								className={isCurrent ? 'button button-active' : 'button'}
							>
								{option.label}
							</button>
						);
					})}
				</div>
			</nav>

			{error !== null && (
				<p className="status-error" role="alert" aria-live="assertive">
					Unable to load meals: {error}
				</p>
			)}

			{isLoading && analytics === null && error === null && (
				<p className="status-loading" aria-live="polite">Loading meal analytics…</p>
			)}

			{analytics !== null && (
				<div className="content-stack">
					{showPage('summary') && <SummarySection analytics={analytics} />}
					{showPage('predictions') && <PredictionsSection analytics={analytics} />}
					{showPage('heatmaps') && <HeatmapsSection analytics={analytics} />}
					{showPage('meals') && <MealsSection analytics={analytics} />}
					{showPage('dailyMenus') && <DailyMenusSection analytics={analytics} />}
					{showPage('mealOccurrences') && <MealOccurrencesSection analytics={analytics} />}
				</div>
			)}
		</main>
	);
}

export default App;
