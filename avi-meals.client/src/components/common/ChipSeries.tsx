type ChipSeriesProps = {
	items: string[];
};

export function ChipSeries({ items }: ChipSeriesProps) {
	return (
		<span className="chip-series">
			{items.map((item, index) => (
				<span key={`${item}-${index}`} className="chip">{item}</span>
			))}
		</span>
	);
}
