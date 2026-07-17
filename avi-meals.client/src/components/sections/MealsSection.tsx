import { ChipSeries } from '../common/ChipSeries';
import { ExternalLink } from '../common/ExternalLink';
import type { MealAnalyticsResponse } from '../../types';
import { formatCurrency } from '../../utils/format';

type MealsSectionProps = {
	analytics: MealAnalyticsResponse;
};

export function MealsSection({ analytics }: MealsSectionProps) {
	if (analytics.meals.length === 0) {
		return (
			<section className="panel">
				<h2>Meals</h2>
				<p>No meals are currently available from the public menu.</p>
			</section>
		);
	}

	return (
		<section className="panel">
			<h2>Meals</h2>
			<div className="table-wrap">
				<table className="data-table">
					<thead>
						<tr>
							<th scope="col">Category</th>
							<th scope="col">Meal</th>
							<th scope="col">Price</th>
							<th scope="col">Description</th>
							<th scope="col">Keywords</th>
							<th scope="col">Link</th>
						</tr>
					</thead>
					<tbody>
						{analytics.meals.map(meal => (
							<tr key={`${meal.category}-${meal.name}`}>
								<td>{meal.category}</td>
								<td>{meal.name}</td>
								<td>{meal.priceLabel || formatCurrency(meal.price)}</td>
								<td>{meal.description}</td>
								<td><ChipSeries items={meal.keywords} /></td>
								<td><ExternalLink href={meal.productUrl} label="Open" /></td>
							</tr>
						))}
					</tbody>
				</table>
			</div>
		</section>
	);
}
