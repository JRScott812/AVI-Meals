import { ChipSeries } from '../common/ChipSeries';
import type { MealAnalyticsResponse } from '../../types';

/**
 * Props for `PredictionsSection`.
 */
type PredictionsSectionProps = {
	/**
	 * Full analytics payload containing prediction collections.
	 */
	analytics: MealAnalyticsResponse;
	/**
	 * Shared currency formatter callback from the app shell.
	 */
	formatCurrency: (value: number) => string;
};

/**
 * Displays model predictions and unannounced meal candidates.
 */
export function PredictionsSection({ analytics, formatCurrency }: PredictionsSectionProps) {
	return (
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
									<td>{formatCurrency(prediction.predictedPrice)}</td>
									<td>{(prediction.confidence * 100).toFixed(0)}%</td>
									<td><ChipSeries items={prediction.keywords} /></td>
									<td>{prediction.rationale}</td>
								</tr>
							))}
						</tbody>
					</table>
				</div>
			</section>
		</>
	);
}
