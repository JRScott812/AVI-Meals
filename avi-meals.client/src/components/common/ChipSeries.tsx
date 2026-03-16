/**
 * Props for `ChipSeries`.
 */
type ChipSeriesProps = {
	/**
	 * Ordered labels to render as visual chips.
	 */
	items: string[];
};

/**
 * Renders a list of short labels as styled chips.
 */
export function ChipSeries({ items }: ChipSeriesProps) {
	return (
		<span className="chip-series">
			{items.map((item, index) => (
				<span key={`${item}-${index}`} className="chip">{item}</span>
			))}
		</span>
	);
}
