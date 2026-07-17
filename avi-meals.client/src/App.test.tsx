import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
						{ label: 'Under $10', value: 1, bucket: 4 }
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
	],
	dailyMenus: [
		{
			date: '2026-01-05',
			items: [
				{
					mealName: 'Monday Veggie Bowl',
					station: 'Main Line',
					category: 'Lunch',
					price: 10.25,
					tags: ['vegan', 'contains soy']
				}
			]
		}
	],
	mealOccurrences: [
		{
			mealName: 'Monday Veggie Bowl',
			occurrenceCount: 2
		}
	]
};

afterEach(() => {
	cleanup();
	vi.restoreAllMocks();
	vi.unstubAllGlobals();
});

describe('App', () => {
	it('loads analytics and shows summary by default', async () => {
		const fetchMock = vi.fn().mockResolvedValue({
			ok: true,
			json: async () => mockAnalyticsResponse
		});
		vi.stubGlobal('fetch', fetchMock);

		render(<App />);

		await waitFor(() => {
			expect(screen.getByRole('heading', { name: 'Summary' })).toBeInTheDocument();
		});

		expect(fetchMock).toHaveBeenCalledWith('/api/meals', expect.objectContaining({ cache: 'no-store' }));
		expect(screen.getByText(/Total meals/i)).toBeInTheDocument();
		expect(screen.getByText(/Predicted unannounced meals/i)).toBeInTheDocument();
		expect(screen.getByText(/^Legend$/)).toBeInTheDocument();
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

		const pagesNav = screen.getAllByText(/^Pages$/)[0].closest('nav');
		expect(pagesNav).not.toBeNull();

		const mealsPageButton = within(pagesNav!).getByRole('button', { name: 'Meals' });
		fireEvent.click(mealsPageButton);

		expect(within(pagesNav!).getByRole('button', { name: 'Meals' })).toHaveAttribute('aria-current', 'page');
		expect(within(pagesNav!).getByRole('button', { name: 'Summary' })).not.toHaveAttribute('aria-current');
	});

	it('shows an empty-state daily menus panel when the payload does not include dailyMenus', async () => {
		vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
			ok: true,
			json: async () => {
				const { dailyMenus, ...legacyPayload } = mockAnalyticsResponse;
				void dailyMenus;
				return legacyPayload;
			}
		}));

		render(<App />);

		await waitFor(() => {
			expect(screen.getByRole('heading', { name: 'Summary' })).toBeInTheDocument();
		});

		const pagesNav = screen.getAllByText(/^Pages$/)[0].closest('nav');
		expect(pagesNav).not.toBeNull();

		const dailyMenusButton = within(pagesNav!).getByRole('button', { name: 'Daily menus' });
		fireEvent.click(dailyMenusButton);

		expect(screen.getByRole('heading', { name: 'Daily menus' })).toBeInTheDocument();
		expect(screen.getByText(/No daily menus are currently available from AVI Dish/i)).toBeInTheDocument();
	});
});
