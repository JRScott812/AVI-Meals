import { describe, expect, it } from 'vitest';
import { getTagChipClassName, getTagTone } from './tagTone';

describe('getTagTone', () => {
	it('classifies dietary preferences', () => {
		expect(getTagTone('Vegan')).toBe('vegan');
		expect(getTagTone('Vegetarian')).toBe('vegetarian');
		expect(getTagTone('Gluten Free')).toBe('gluten-free');
		expect(getTagTone('gluten-free')).toBe('gluten-free');
	});

	it('classifies allergen tags by type', () => {
		expect(getTagTone('Contains Soy')).toBe('allergen-soy');
		expect(getTagTone('Contains Milk')).toBe('allergen-dairy');
		expect(getTagTone('Contains Tree Nuts')).toBe('allergen-nut');
		expect(getTagTone('Contains Fish')).toBe('allergen-seafood');
		expect(getTagTone('Contains Sesame')).toBe('allergen-sesame');
		expect(getTagTone('Contains Mystery')).toBe('allergen');
	});

	it('classifies meal periods', () => {
		expect(getTagTone('Breakfast')).toBe('breakfast');
		expect(getTagTone('Brunch')).toBe('brunch');
		expect(getTagTone('Lunch')).toBe('lunch');
		expect(getTagTone('Dinner')).toBe('dinner');
	});

	it('maps empty and none labels', () => {
		expect(getTagTone('none')).toBe('none');
		expect(getTagTone('')).toBe('none');
	});

	it('builds chip class names', () => {
		expect(getTagChipClassName('Vegan')).toBe('chip chip-vegan');
		expect(getTagChipClassName('Contains Soy')).toBe('chip chip-allergen-soy');
	});
});
