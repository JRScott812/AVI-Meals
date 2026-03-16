import type { HeatmapCell, MealAnalyticsResponse } from '../../types';

/**
 * Props for `HeatmapsSection`.
 */
type HeatmapsSectionProps = {
	/**
	 * Full analytics payload containing computed heatmaps.
	 */
	analytics: MealAnalyticsResponse;
};

/**
 * Displays all heatmap tables available in the analytics payload.
 */
export function HeatmapsSection({ analytics }: HeatmapsSectionProps) {
	const legendItems = [
		{ bucket: 0, label: '0 (none)' },
		{ bucket: 1, label: '1 (low)' },
		{ bucket: 2, label: '2 (moderate)' },
		{ bucket: 3, label: '3 (high)' },
		{ bucket: 4, label: '4 (peak)' }
	];

	const getHeatmapCellClassName = (cell: HeatmapCell): string => {
		const bucket = Math.max(0, Math.min(4, cell.bucket));
		return `heatmap-cell heatmap-cell-${bucket}`;
	};

	return (
		<section className="panel">
			<h2>Heatmaps</h2>
			<div className="heatmap-legend" aria-label="Heatmap legend">
				<span className="heatmap-legend-label">Legend</span>
				<div className="heatmap-legend-items">
					{legendItems.map(item => (
						<span key={item.bucket} className={`heatmap-legend-item heatmap-cell-${item.bucket}`}>{item.label}</span>
					))}
				</div>
			</div>
			<div className="content-stack">
				{analytics.heatmaps.map(heatmap => (
					<article key={heatmap.title} className="sub-panel">
						<h3>{heatmap.title}</h3>
						<div className="table-wrap">
							<table className="data-table heatmap-table">
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
												<td key={`${row.label}-${cell.label}`} className={getHeatmapCellClassName(cell)} title={`${cell.label}: ${cell.value}`}>
													<span>{cell.value}</span>
												</td>
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
	);
}
