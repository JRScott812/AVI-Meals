const currency = new Intl.NumberFormat('en-US', {
	style: 'currency',
	currency: 'USD',
});

export function formatCurrency(value: number): string {
	return currency.format(value);
}

export function formatConfidence(value: number): string {
	return `${(value * 100).toFixed(0)}%`;
}
