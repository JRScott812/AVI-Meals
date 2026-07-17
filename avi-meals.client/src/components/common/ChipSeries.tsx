import { getTagChipClassName } from '../../utils/tagTone';

type ChipSeriesProps = {
	items: string[];
	/** When true, apply dietary / allergen color coding. */
	colored?: boolean;
};

export function ChipSeries({ items, colored = false }: ChipSeriesProps) {
	return (
		<span className="chip-series">
			{items.map((item, index) => (
				<span
					key={`${item}-${index}`}
					className={colored ? getTagChipClassName(item) : 'chip'}
				>
					{item}
				</span>
			))}
		</span>
	);
}
