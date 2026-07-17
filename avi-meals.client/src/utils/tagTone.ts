export type TagTone =
	| 'default'
	| 'none'
	| 'vegan'
	| 'vegetarian'
	| 'gluten-free'
	| 'breakfast'
	| 'brunch'
	| 'lunch'
	| 'dinner'
	| 'allergen'
	| 'allergen-dairy'
	| 'allergen-egg'
	| 'allergen-soy'
	| 'allergen-wheat'
	| 'allergen-nut'
	| 'allergen-seafood'
	| 'allergen-sesame';

/**
 * Maps Dish preference / allergen / meal-period labels to visual tones.
 */
export function getTagTone(label: string): TagTone {
	const normalized = label.trim().toLowerCase().replace(/[_-]+/g, ' ').replace(/\s+/g, ' ');

	if (normalized === '' || normalized === 'none') {
		return 'none';
	}

	if (normalized.startsWith('contains ')) {
		return getAllergenTone(normalized.slice('contains '.length));
	}

	if (normalized.includes('vegan')) {
		return 'vegan';
	}

	if (normalized.includes('vegetarian') || normalized === 'veggie') {
		return 'vegetarian';
	}

	if (normalized.includes('gluten free') || normalized.includes('glutenfree') || normalized === 'gf') {
		return 'gluten-free';
	}

	if (normalized === 'breakfast') {
		return 'breakfast';
	}

	if (normalized === 'brunch') {
		return 'brunch';
	}

	if (normalized === 'lunch') {
		return 'lunch';
	}

	if (normalized === 'dinner') {
		return 'dinner';
	}

	return 'default';
}

function getAllergenTone(allergenName: string): TagTone {
	const name = allergenName.trim().toLowerCase();

	if (name.includes('milk') || name.includes('dairy') || name.includes('lactose')) {
		return 'allergen-dairy';
	}

	if (name.includes('egg')) {
		return 'allergen-egg';
	}

	if (name.includes('soy')) {
		return 'allergen-soy';
	}

	if (name.includes('wheat') || name.includes('gluten')) {
		return 'allergen-wheat';
	}

	if (name.includes('peanut') || name.includes('tree nut') || name.includes('nut')) {
		return 'allergen-nut';
	}

	if (name.includes('fish') || name.includes('shellfish') || name.includes('crustacean')) {
		return 'allergen-seafood';
	}

	if (name.includes('sesame')) {
		return 'allergen-sesame';
	}

	return 'allergen';
}

export function getTagChipClassName(label: string): string {
	return `chip chip-${getTagTone(label)}`;
}
