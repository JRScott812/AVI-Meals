import { useCallback, useEffect, useState } from 'react';
import './App.css';

interface MealSummary {
	mealCount: number;
	categoryCount: number;
	lowestPrice: number;
	highestPrice: number;
	averagePrice: number;
	categoryNames: string[];
}

interface MealItem {
	category: string;
	name: string;
	description: string;
	price: number;
	priceLabel: string;
	productUrl: string;
	keywords: string[];
}

interface HeatmapCell {
	label: string;
	value: number;
	bucket: number;
	shade: string;
}

interface HeatmapRow {
	label: string;
	cells: HeatmapCell[];
}

interface Heatmap {
	title: string;
	columns: string[];
	rows: HeatmapRow[];
}

interface Prediction {
	title: string;
	detail: string;
	confidence: number;
}

interface UnannouncedMealPrediction {
	name: string;
	category: string;
	rationale: string;
	predictedPrice: number;
	confidence: number;
	keywords: string[];
}

interface MealAnalyticsResponse {
	portalUrl: string;
	diningUrl: string;
	menuUrl: string;
	retrievedAtUtc: string;
	summary: MealSummary;
	meals: MealItem[];
	heatmaps: Heatmap[];
	predictions: Prediction[];
	unannouncedMealPredictions: UnannouncedMealPrediction[];
}

const currency = new Intl.NumberFormat('en-US', {
	style: 'currency',
	currency: 'USD',
});

const transientStatusCodes = new Set([502, 503, 504]);
const maxStartupRetries = 6;
const startupRetryDelayMs = 750;

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

type DashboardPage = 'summary' | 'predictions' | 'heatmaps' | 'meals' | 'all';

function App() {
	const [analytics, setAnalytics] = useState<MealAnalyticsResponse | null>(null);
	const [error, setError] = useState<string | null>(null);
	const [isLoading, setIsLoading] = useState(true);
	const [page, setPage] = useState<DashboardPage>('all');

	const loadAnalytics = useCallback(async (signal?: AbortSignal) => {
		setIsLoading(true);
		setError(null);

		try {
			for (let attempt = 0; attempt <= maxStartupRetries; attempt++) {
				const response = await fetch('/api/meals', { signal, cache: 'no-store' });
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

	const showPage = (target: DashboardPage): boolean => page === 'all' || page === target;

	return (
		<main>
			<h1>AVI Meals</h1>
			<p>Reads the Taylor dining portal, follows its public menu links, and builds meal heatmaps and predictions.</p>
			<p>
				<button type="button" onClick={() => void loadAnalytics()} disabled={isLoading}>
					{isLoading ? 'Loading...' : 'Refresh meals'}
				</button>
			</p>

			<nav>
				<p>Pages:</p>
				<p>
					<button type="button" onClick={() => setPage('all')} disabled={page === 'all'}>All</button>{' '}
					<button type="button" onClick={() => setPage('summary')} disabled={page === 'summary'}>Summary</button>{' '}
					<button type="button" onClick={() => setPage('predictions')} disabled={page === 'predictions'}>Predictions</button>{' '}
					<button type="button" onClick={() => setPage('heatmaps')} disabled={page === 'heatmaps'}>Heatmaps</button>{' '}
					<button type="button" onClick={() => setPage('meals')} disabled={page === 'meals'}>Meals</button>
				</p>
			</nav>

			{error !== null && <p>Unable to load meals: {error}</p>}

			{analytics !== null && (
				<>
					{showPage('summary') && (
						<section>
							<h2>Summary</h2>
							<ul>
								<li>Total meals: {analytics.summary.mealCount}</li>
								<li>Total categories: {analytics.summary.categoryCount}</li>
								<li>Lowest price: {currency.format(analytics.summary.lowestPrice)}</li>
								<li>Highest price: {currency.format(analytics.summary.highestPrice)}</li>
								<li>Average price: {currency.format(analytics.summary.averagePrice)}</li>
								<li>Categories: {analytics.summary.categoryNames.join(', ')}</li>
								<li>Portal source: <a href={analytics.portalUrl} target="_blank" rel="noreferrer">Taylor dining portal</a></li>
								<li>Dining page: <a href={analytics.diningUrl} target="_blank" rel="noreferrer">AVI dining page</a></li>
								<li>Menu page: <a href={analytics.menuUrl} target="_blank" rel="noreferrer">CaterTrax menu</a></li>
								<li>Snapshot time: {new Date(analytics.retrievedAtUtc).toLocaleString()}</li>
							</ul>
						</section>
					)}

					{showPage('predictions') && (
						<>
							<section>
								<h2>Predictions</h2>
								<ol>
									{analytics.predictions.map(prediction => (
										<li key={prediction.title}>
											<strong>{prediction.title}</strong>
											<div>{prediction.detail}</div>
											<div>Confidence: {(prediction.confidence * 100).toFixed(0)}%</div>
										</li>
									))}
								</ol>
							</section>

							<section>
								<h2>Predicted unannounced meals</h2>
								<table border={1} cellPadding={6}>
									<thead>
										<tr>
											<th>Name</th>
											<th>Category</th>
											<th>Predicted price</th>
											<th>Confidence</th>
											<th>Keywords</th>
											<th>Rationale</th>
										</tr>
									</thead>
									<tbody>
										{analytics.unannouncedMealPredictions.map(prediction => (
											<tr key={prediction.name}>
												<td>{prediction.name}</td>
												<td>{prediction.category}</td>
												<td>{currency.format(prediction.predictedPrice)}</td>
												<td>{(prediction.confidence * 100).toFixed(0)}%</td>
												<td>{prediction.keywords.join(', ')}</td>
												<td>{prediction.rationale}</td>
											</tr>
										))}
									</tbody>
								</table>
							</section>
						</>
					)}

					{showPage('heatmaps') && (
						<section>
							<h2>Heatmaps</h2>
							{analytics.heatmaps.map(heatmap => (
								<article key={heatmap.title}>
									<h3>{heatmap.title}</h3>
									<table border={1} cellPadding={6}>
										<thead>
											<tr>
												<th>Category</th>
												{heatmap.columns.map(column => (
													<th key={column}>{column}</th>
												))}
											</tr>
										</thead>
										<tbody>
											{heatmap.rows.map(row => (
												<tr key={row.label}>
													<th>{row.label}</th>
													{row.cells.map(cell => (
														<td key={`${row.label}-${cell.label}`}>
															{cell.shade} {cell.value}
														</td>
													))}
												</tr>
											))}
										</tbody>
									</table>
								</article>
							))}
						</section>
					)}

					{showPage('meals') && (
						<section>
							<h2>Meals</h2>
							<table border={1} cellPadding={6}>
								<thead>
									<tr>
										<th>Category</th>
										<th>Meal</th>
										<th>Price</th>
										<th>Description</th>
										<th>Keywords</th>
										<th>Link</th>
									</tr>
								</thead>
								<tbody>
									{analytics.meals.map(meal => (
										<tr key={`${meal.category}-${meal.name}`}>
											<td>{meal.category}</td>
											<td>{meal.name}</td>
											<td>{meal.priceLabel || currency.format(meal.price)}</td>
											<td>{meal.description}</td>
											<td>{meal.keywords.join(', ')}</td>
											<td>
												<a href={meal.productUrl} target="_blank" rel="noreferrer">
													Open
												</a>
											</td>
										</tr>
									))}
								</tbody>
							</table>
						</section>
					)}
				</>
			)}
		</main>
	);
}

export default App;