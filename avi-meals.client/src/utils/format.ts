const currency = new Intl.NumberFormat('en-US', {
	style: 'currency',
	currency: 'USD',
});

const enumLabels: Record<string, string> = {
	AppetizerDisplays: 'Appetizer Displays',
	BoxedLunch: 'Boxed Lunch',
	HotBuffets: 'Hot Buffets',
	SandwichAndSaladBuffets: 'Sandwich and Salad Buffets',
	NutriBar: 'NutriBar',
	GrillAndSpecials: 'Grill & Specials',
	GrillTakeOver: 'Grill Take Over',
	DeliAndFeatures: 'Deli & Features',
	YogurtBar: 'Yogurt Bar',
	TopAndToast: 'Top & Toast',
	HotCereals: 'Hot Cereals',
	HotBreakfastSpecials: 'Hot Breakfast Specials',
	FoodTruck: 'Taylor Food Truck',
	TakeOverTopping: 'Take Over Topping',
	Grill1846: '1846 Grill',
	MainLine: 'Main Line',
};

export function formatCurrency(value: number): string {
	return currency.format(value);
}

export function formatConfidence(value: number): string {
	return `${(value * 100).toFixed(0)}%`;
}

export function formatEnumLabel(value: string): string {
	if (enumLabels[value]) {
		return enumLabels[value];
	}

	return value.replace(/([a-z\d])([A-Z])/g, '$1 $2');
}
