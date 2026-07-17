import { ChipSeries } from '../common/ChipSeries';
import type { MealAnalyticsResponse } from '../../types';
import { formatEnumLabel } from '../../utils/format';

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
			<p className="panel-note">
				Duplicate dish names are combined. Occurrence count is how many times each dish appears across Hodson history, with the meal periods where it is offered.
			</p>
			<div className="table-wrap">
				<table className="data-table">
					<thead>
						<tr>
							<th scope="col">Meal</th>
							<th scope="col">Occurrence count</th>
							<th scope="col">Offered during</th>
						</tr>
					</thead>
					<tbody>
						{mealOccurrences.map(item => {
							const mealTypes = Array.isArray(item.mealTypes) ? item.mealTypes : [];
							return (
								<tr key={item.mealName}>
									<td>{item.mealName}</td>
									<td>{item.occurrenceCount}</td>
									<td>
										<ChipSeries
											items={mealTypes.length === 0
												? ['none']
												: mealTypes.map(formatEnumLabel)}
											colored
										/>
									</td>
								</tr>
							);
						})}
					</tbody>
				</table>
			</div>
		</section>
	);
}
