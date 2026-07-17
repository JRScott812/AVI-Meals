import { ChipSeries } from '../common/ChipSeries';
import { ExternalLink } from '../common/ExternalLink';
import type { MealAnalyticsResponse } from '../../types';
import { formatCurrency, formatEnumLabel } from '../../utils/format';

type SummarySectionProps = {
	analytics: MealAnalyticsResponse;
};

export function SummarySection({ analytics }: SummarySectionProps) {
	return (
		<section className="panel">
			<h2>Summary</h2>
			<ul className="summary-list">
				<li><span>Total meals</span><strong>{analytics.summary.mealCount}</strong></li>
				<li><span>Total categories</span><strong>{analytics.summary.categoryCount}</strong></li>
				<li><span>Lowest price</span><strong>{formatCurrency(analytics.summary.lowestPrice)}</strong></li>
				<li><span>Highest price</span><strong>{formatCurrency(analytics.summary.highestPrice)}</strong></li>
				<li><span>Average price</span><strong>{formatCurrency(analytics.summary.averagePrice)}</strong></li>
				<li><span>Categories</span><ChipSeries items={analytics.summary.categories.map(formatEnumLabel)} /></li>
				<li><span>Portal source</span><ExternalLink href={analytics.portalUrl} label="Taylor dining portal" /></li>
				<li><span>Dining page</span><ExternalLink href={analytics.diningUrl} label="AVI dining page" /></li>
				<li><span>Menu page</span><ExternalLink href={analytics.menuUrl} label="CaterTrax menu" /></li>
				<li><span>Snapshot time</span><strong>{new Date(analytics.retrievedAtUtc).toLocaleString()}</strong></li>
			</ul>
		</section>
	);
}
