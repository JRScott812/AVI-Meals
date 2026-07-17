import { ChipSeries } from '../common/ChipSeries';
import type { MealAnalyticsResponse } from '../../types';
import { formatConfidence, formatEnumLabel } from '../../utils/format';

type PredictionsSectionProps = {
	analytics: MealAnalyticsResponse;
};

export function PredictionsSection({ analytics }: PredictionsSectionProps) {
	const hasPredictions = analytics.predictions.length > 0;
	const hasUnannounced = analytics.unannouncedMealPredictions.length > 0;

	return (
		<>
			<section className="panel">
				<h2>Predictions</h2>
				{!hasPredictions ? (
					<p>No predictions are available for the current menu.</p>
				) : (
					<ol className="prediction-list">
						{analytics.predictions.map(prediction => (
							<li key={prediction.title}>
								<strong>{prediction.title}</strong>
								<p>{prediction.detail}</p>
								<span className="pill">Confidence: {formatConfidence(prediction.confidence)}</span>
							</li>
						))}
					</ol>
				)}
			</section>

			<section className="panel">
				<h2>Predicted unannounced meals</h2>
				{!hasUnannounced ? (
					<p>No unannounced meal predictions are available.</p>
				) : (
					<div className="table-wrap">
						<table className="data-table">
							<thead>
								<tr>
									<th scope="col">Name</th>
									<th scope="col">Station</th>
									<th scope="col">Meal</th>
									<th scope="col">Category</th>
									<th scope="col">Confidence</th>
									<th scope="col">Keywords</th>
									<th scope="col">Rationale</th>
								</tr>
							</thead>
							<tbody>
								{analytics.unannouncedMealPredictions.map(prediction => (
									<tr key={prediction.name}>
										<td>{prediction.name}</td>
										<td>{formatEnumLabel(prediction.station)}</td>
										<td>{formatEnumLabel(prediction.mealType)}</td>
										<td>{prediction.category}</td>
										<td>{formatConfidence(prediction.confidence)}</td>
										<td><ChipSeries items={prediction.keywords} /></td>
										<td>{prediction.rationale}</td>
									</tr>
								))}
							</tbody>
						</table>
					</div>
				)}
			</section>
		</>
	);
}
