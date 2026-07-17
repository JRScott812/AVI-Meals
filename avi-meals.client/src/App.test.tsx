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
		categories: ['Breakfast']
	},
	meals: [
		{
			category: 'Breakfast',
			name: 'Continental Breakfast Buffet',
			description: 'Fruit and pastries.',
			price: 8.99,
			productUrl: 'https://example.com/1',
			keywords: ['fruit', 'pastries']
		}
	],
	heatmaps: [
		{
			title: 'Station vs meal type',
			rowHeader: 'Station',
			columns: ['Lunch'],
			rows: [
				{
					label: 'Main Line',
					cells: [
						{ label: 'Lunch', value: 1, bucket: 4 }
					]
				}
			]
		}
	],
	predictions: [
		{
			title: 'Most active Hodson station',
			detail: 'Main Line accounts for most plated items.',
			confidence: 0.8
		}
	],
	unannouncedMealPredictions: [
		{
			name: 'Citrus Chicken Skillet',
			station: 'MainLine',
			mealType: 'Lunch',
			category: 'Pizza & Pasta',
			rationale: 'Recurring keywords indicate this style.',
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
					station: 'MainLine',
					mealType: 'Lunch',
					category: 'Pizza & Pasta',
					tags: ['vegan', 'contains soy']
				}
			]
		}
	],
	mealOccurrences: [
		{
			mealName: 'Monday Veggie Bowl',
			occurrenceCount: 2,
			mealTypes: ['Lunch', 'Dinner']
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
		expect(screen.getByRole('button', { name: /Dark mode|Light mode/i })).toBeInTheDocument();
	});

	it('toggles dark mode and persists the preference', async () => {
		vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
			ok: true,
			json: async () => mockAnalyticsResponse
		}));

		const storage = new Map<string, string>();
		vi.stubGlobal('localStorage', {
			getItem: (key: string) => storage.get(key) ?? null,
			setItem: (key: string, value: string) => {
				storage.set(key, value);
			},
			removeItem: (key: string) => {
				storage.delete(key);
			},
			clear: () => {
				storage.clear();
			},
			key: () => null,
			length: 0
		});

		render(<App />);

		await waitFor(() => {
			expect(screen.getByRole('heading', { name: 'Summary' })).toBeInTheDocument();
		});

		const themeButton = screen.getByRole('button', { name: /Switch to dark mode|Dark mode/i });
		fireEvent.click(themeButton);

		expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
		expect(storage.get('avi-meals-theme')).toBe('dark');
		expect(screen.getByRole('button', { name: /Switch to light mode|Light mode/i })).toBeInTheDocument();
	});

	it('switches to catering catalog page when the nav button is clicked', async () => {
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

		const mealsPageButton = within(pagesNav!).getByRole('button', { name: 'Catering catalog' });
		fireEvent.click(mealsPageButton);

		expect(within(pagesNav!).getByRole('button', { name: 'Catering catalog' })).toHaveAttribute('aria-current', 'page');
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

	it('expands a daily menu day when toggled', async () => {
		vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
			ok: true,
			json: async () => mockAnalyticsResponse
		}));

		render(<App />);

		await waitFor(() => {
			expect(screen.getByRole('heading', { name: 'Summary' })).toBeInTheDocument();
		});

		const pagesNav = screen.getAllByText(/^Pages$/)[0].closest('nav');
		fireEvent.click(within(pagesNav!).getByRole('button', { name: 'Daily menus' }));

		expect(screen.getByText(/1 day of Hodson residential history/i)).toBeInTheDocument();
		const dayToggle = screen.getByRole('button', { name: /1 item/i });
		fireEvent.click(dayToggle);
		expect(screen.getByText('Main Line')).toBeInTheDocument();
		expect(screen.getByText('Monday Veggie Bowl')).toBeInTheDocument();
		expect(screen.getByText('vegan').className).toContain('chip-vegan');
		expect(screen.getByText('contains soy').className).toContain('chip-allergen-soy');
	});
});
