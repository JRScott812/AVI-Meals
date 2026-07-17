import { useMemo, useState } from 'react';
import { ChipSeries } from '../common/ChipSeries';
import type { MealAnalyticsResponse } from '../../types';
import { formatCurrency } from '../../utils/format';

type DailyMenusSectionProps = {
	analytics: MealAnalyticsResponse;
};

export function DailyMenusSection({ analytics }: DailyMenusSectionProps) {
	const [expandedDates, setExpandedDates] = useState<Set<string>>(() => new Set());

	const sortedMenus = useMemo(() => {
		const dailyMenus = Array.isArray(analytics.dailyMenus) ? analytics.dailyMenus : [];
		return [...dailyMenus].sort((a, b) => b.date.localeCompare(a.date));
	}, [analytics.dailyMenus]);

	if (sortedMenus.length === 0) {
		return (
			<section className="panel">
				<h2>Daily menus</h2>
				<p>No daily menus are currently available from AVI Dish.</p>
			</section>
		);
	}

	const toggleDate = (date: string) => {
		setExpandedDates(current => {
			const next = new Set(current);
			if (next.has(date)) {
				next.delete(date);
			} else {
				next.add(date);
			}
			return next;
		});
	};

	return (
		<section className="panel">
			<h2>Daily menus</h2>
			<p className="panel-note">{sortedMenus.length} day{sortedMenus.length === 1 ? '' : 's'} in history. Expand a day to view items.</p>
			<div className="content-stack">
				{sortedMenus.map(day => {
					const isExpanded = expandedDates.has(day.date);
					const itemCount = day.items?.length ?? 0;
					return (
						<article key={day.date} className="sub-panel">
							<button
								type="button"
								className="button day-toggle"
								aria-expanded={isExpanded}
								onClick={() => toggleDate(day.date)}
							>
								{new Date(day.date).toLocaleDateString()} — {itemCount} item{itemCount === 1 ? '' : 's'}
							</button>
							{isExpanded && (
								<div className="table-wrap">
									<table className="data-table">
										<thead>
											<tr>
												<th scope="col">Meal</th>
												<th scope="col">Station</th>
												<th scope="col">Category</th>
												<th scope="col">Price</th>
												<th scope="col">Tags</th>
											</tr>
										</thead>
										<tbody>
											{(day.items ?? []).map((item, index) => (
												<tr key={`${day.date}-${item.mealName}-${index}`}>
													<td>{item.mealName}</td>
													<td>{item.station}</td>
													<td>{item.category}</td>
													<td>{item.price === undefined ? '—' : formatCurrency(item.price)}</td>
													<td><ChipSeries items={item.tags.length === 0 ? ['none'] : item.tags} /></td>
												</tr>
											))}
										</tbody>
									</table>
								</div>
							)}
						</article>
					);
				})}
			</div>
		</section>
	);
}
