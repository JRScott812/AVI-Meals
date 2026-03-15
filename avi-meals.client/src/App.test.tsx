import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import App from './App';

const mockAnalyticsResponse = {
	portalUrl: 'https://connecttaylor.atriumcampus.com/index.php',
	diningUrl: 'https://aviserves.com/taylor/meal-plans-and-dining.html',
	menuUrl: 'https://tayloru.catertrax.com/menugrid.asp?mode=aff',
	retrievedAtUtc: '2026-01-01T00:00:00Z',
	summary: {
		mealCount: 2,
		categoryCount: 1,
		lowestPrice: 8.99,
		highestPrice: 10.99,
		averagePrice: 9.99,
		categoryNames: ['Breakfast']
	},
	meals: [
		{
			category: 'Breakfast',
			name: 'Continental Breakfast Buffet',
			description: 'Fruit and pastries.',
			price: 8.99,
			priceLabel: '$8.99',
			productUrl: 'https://example.com/1',
			keywords: ['fruit', 'pastries']
		}
	],
	heatmaps: [
		{
			title: 'Category vs price band',
			columns: ['Under $10'],
			rows: [
				{
					label: 'Breakfast',
					cells: [
						{ label: 'Under $10', value: 1, bucket: 4, shade: '█' }
					]
				}
			]
		}
	],
	predictions: [
		{
			title: 'Most likely menu focus',
			detail: 'Breakfast has the deepest lineup.',
			confidence: 0.8
		}
	],
	unannouncedMealPredictions: [
		{
			name: 'Citrus Chicken Skillet',
			category: 'Breakfast',
			rationale: 'Recurring keywords indicate this style.',
			predictedPrice: 10.5,
			confidence: 0.68,
			keywords: ['citrus', 'chicken', 'skillet']
		}
	]
};

afterEach(() => {
	vi.restoreAllMocks();
	vi.unstubAllGlobals();
});

describe('App', () => {
	it('loads analytics and shows summary by default', async () => {
		vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
			ok: true,
			json: async () => mockAnalyticsResponse
		}));

		render(<App />);

		await waitFor(() => {
			expect(screen.getByRole('heading', { name: 'Summary' })).toBeInTheDocument();
		});

		expect(screen.getByText(/Total meals:/)).toBeInTheDocument();
		expect(screen.getByText(/Predicted unannounced meals/)).toBeInTheDocument();
	});

	it('switches to meals page when the Meals button is clicked', async () => {
		vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
			ok: true,
			json: async () => mockAnalyticsResponse
		}));

		render(<App />);

		await waitFor(() => {
			expect(screen.getByRole('heading', { name: 'Summary' })).toBeInTheDocument();
		});

		const pagesNav = screen.getAllByText(/^Pages:$/)[0].closest('nav');
		expect(pagesNav).not.toBeNull();

		const mealsPageButton = within(pagesNav!).getByRole('button', { name: 'Meals' });
		fireEvent.click(mealsPageButton);

		expect(within(pagesNav!).getByRole('button', { name: 'Meals' })).toBeDisabled();
		expect(within(pagesNav!).getByRole('button', { name: 'Summary' })).toBeEnabled();
	});
});
