import { ChipSeries } from '../common/ChipSeries';
import { ExternalLink } from '../common/ExternalLink';
import type { MealAnalyticsResponse } from '../../types';

/**
 * Props for `MealsSection`.
 */
type MealsSectionProps = {
	/**
	 * Full analytics payload containing the current meal list.
	 */
	analytics: MealAnalyticsResponse;
	/**
	 * Shared currency formatter callback from the app shell.
	 */
	formatCurrency: (value: number) => string;
};

/**
 * Displays the raw meals table with category, pricing, keywords, and source links.
 */
export function MealsSection({ analytics, formatCurrency }: MealsSectionProps) {
	return (
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
