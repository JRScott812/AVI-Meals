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

	const renderChipSeries = (items: string[]) => (
		<span className="chip-series">
			{items.map((item, index) => (
				<span key={`${item}-${index}`} className="chip">{item}</span>
			))}
		</span>
	);

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
		<main className="app-shell">
			<header className="hero">
				<div className="brand-lockup">
					<img src="/avi-taylor-mark.svg" alt="Taylor AVI mark" className="brand-mark" />
					<div>
						<p className="brand-kicker">Taylor Dining by AVI Fresh</p>
						<h1>AVI Meals</h1>
					</div>
				</div>
				<p>Reads the Taylor dining portal, follows its public menu links, and builds meal heatmaps and predictions.</p>
				<div className="toolbar">
					<button type="button" onClick={() => void loadAnalytics()} disabled={isLoading} className="button button-primary">
						{isLoading ? 'Loading...' : 'Refresh meals'}
					</button>
				</div>
			</header>

			<nav className="page-nav">
				<p className="page-nav-label">Pages</p>
				<div className="page-nav-buttons">
					<button type="button" onClick={() => setPage('all')} disabled={page === 'all'} className="button">All</button>
					<button type="button" onClick={() => setPage('summary')} disabled={page === 'summary'} className="button">Summary</button>
					<button type="button" onClick={() => setPage('predictions')} disabled={page === 'predictions'} className="button">Predictions</button>
					<button type="button" onClick={() => setPage('heatmaps')} disabled={page === 'heatmaps'} className="button">Heatmaps</button>
					<button type="button" onClick={() => setPage('meals')} disabled={page === 'meals'} className="button">Meals</button>
				</div>
			</nav>

			{error !== null && <p className="status-error">Unable to load meals: {error}</p>}

			{analytics !== null && (
				<div className="content-stack">
					{showPage('summary') && (
						<section className="panel">
							<h2>Summary</h2>
							<ul className="summary-list">
								<li><span>Total meals</span><strong>{analytics.summary.mealCount}</strong></li>
								<li><span>Total categories</span><strong>{analytics.summary.categoryCount}</strong></li>
								<li><span>Lowest price</span><strong>{currency.format(analytics.summary.lowestPrice)}</strong></li>
								<li><span>Highest price</span><strong>{currency.format(analytics.summary.highestPrice)}</strong></li>
								<li><span>Average price</span><strong>{currency.format(analytics.summary.averagePrice)}</strong></li>
								<li><span>Categories</span>{renderChipSeries(analytics.summary.categoryNames)}</li>
								<li><span>Portal source</span><a href={analytics.portalUrl} target="_blank" rel="noreferrer">Taylor dining portal</a></li>
								<li><span>Dining page</span><a href={analytics.diningUrl} target="_blank" rel="noreferrer">AVI dining page</a></li>
								<li><span>Menu page</span><a href={analytics.menuUrl} target="_blank" rel="noreferrer">CaterTrax menu</a></li>
								<li><span>Snapshot time</span><strong>{new Date(analytics.retrievedAtUtc).toLocaleString()}</strong></li>
							</ul>
						</section>
					)}

					{showPage('predictions') && (
						<>
							<section className="panel">
								<h2>Predictions</h2>
								<ol className="prediction-list">
									{analytics.predictions.map(prediction => (
										<li key={prediction.title}>
											<strong>{prediction.title}</strong>
											<p>{prediction.detail}</p>
											<span className="pill">Confidence: {(prediction.confidence * 100).toFixed(0)}%</span>
										</li>
									))}
								</ol>
							</section>

							<section className="panel">
								<h2>Predicted unannounced meals</h2>
								<div className="table-wrap">
									<table className="data-table">
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
													<td>{renderChipSeries(prediction.keywords)}</td>
													<td>{prediction.rationale}</td>
												</tr>
											))}
										</tbody>
									</table>
								</div>
							</section>
						</>
					)}

					{showPage('heatmaps') && (
						<section className="panel">
							<h2>Heatmaps</h2>
							<div className="content-stack">
								{analytics.heatmaps.map(heatmap => (
									<article key={heatmap.title} className="sub-panel">
										<h3>{heatmap.title}</h3>
										<div className="table-wrap">
											<table className="data-table">
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
																<td key={`${row.label}-${cell.label}`}>{cell.shade} {cell.value}</td>
															))}
														</tr>
													))}
												</tbody>
											</table>
										</div>
									</article>
								))}
							</div>
						</section>
					)}

					{showPage('meals') && (
						<section className="panel">
							<h2>Meals</h2>
							<div className="table-wrap">
								<table className="data-table">
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
												<td>{renderChipSeries(meal.keywords)}</td>
												<td><a href={meal.productUrl} target="_blank" rel="noreferrer">Open</a></td>
											</tr>
										))}
									</tbody>
								</table>
							</div>
						</section>
					)}
				</div>
			)}
		</main>
	);
}

export default App;