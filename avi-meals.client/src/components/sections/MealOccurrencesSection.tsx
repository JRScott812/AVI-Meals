import type { MealAnalyticsResponse } from '../../types';

type MealOccurrencesSectionProps = {
	analytics: MealAnalyticsResponse;
};

export function MealOccurrencesSection({ analytics }: MealOccurrencesSectionProps) {
	const mealOccurrences = Array.isArray(analytics.mealOccurrences) ? analytics.mealOccurrences : [];

	if (mealOccurrences.length === 0) {
		return (
			<section className="panel">
				<h2>Meal occurrence collection</h2>
				<p>No meal occurrence data is available yet.</p>
			</section>
		);
	}

	return (
		<section className="panel">
			<h2>Meal occurrence collection</h2>
			<div className="table-wrap">
				<table className="data-table">
					<thead>
						<tr>
							<th scope="col">Meal</th>
							<th scope="col">Occurrence count</th>
						</tr>
					</thead>
					<tbody>
						{mealOccurrences.map(item => (
							<tr key={item.mealName}>
								<td>{item.mealName}</td>
								<td>{item.occurrenceCount}</td>
							</tr>
						))}
					</tbody>
				</table>
			</div>
		</section>
	);
}
