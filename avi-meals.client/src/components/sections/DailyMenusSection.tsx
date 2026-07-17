import { ChipSeries } from '../common/ChipSeries';
import type { MealAnalyticsResponse } from '../../types';
import { formatCurrency } from '../../utils/format';

type DailyMenusSectionProps = {
	analytics: MealAnalyticsResponse;
};

export function DailyMenusSection({ analytics }: DailyMenusSectionProps) {
	const dailyMenus = Array.isArray(analytics.dailyMenus) ? analytics.dailyMenus : [];

	if (dailyMenus.length === 0) {
		return (
			<section className="panel">
				<h2>Daily menus</h2>
				<p>No daily menus are currently available from AVI Dish.</p>
			</section>
		);
	}

	return (
		<section className="panel">
			<h2>Daily menus</h2>
			<div className="table-wrap">
				<table className="data-table">
					<thead>
						<tr>
							<th scope="col">Date</th>
							<th scope="col">Meal</th>
							<th scope="col">Station</th>
							<th scope="col">Category</th>
							<th scope="col">Price</th>
							<th scope="col">Tags</th>
						</tr>
					</thead>
					<tbody>
						{dailyMenus.flatMap(day =>
							(day.items ?? []).map((item, index) => (
								<tr key={`${day.date}-${item.mealName}-${index}`}>
									<td>{new Date(day.date).toLocaleDateString()}</td>
									<td>{item.mealName}</td>
									<td>{item.station}</td>
									<td>{item.category}</td>
									<td>{item.price === undefined ? '—' : formatCurrency(item.price)}</td>
									<td><ChipSeries items={item.tags.length === 0 ? ['none'] : item.tags} /></td>
								</tr>
							))
						)}
					</tbody>
				</table>
			</div>
		</section>
	);
}
