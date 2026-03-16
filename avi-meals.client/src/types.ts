/**
 * Aggregate meal metrics displayed in the summary section.
 */
export interface MealSummary {
	mealCount: number;
	categoryCount: number;
	lowestPrice: number;
	highestPrice: number;
	averagePrice: number;
	categoryNames: string[];
}

/**
 * A single menu meal item scraped from the source catalog.
 */
export interface MealItem {
	category: string;
	name: string;
	description: string;
	price: number;
	priceLabel: string;
	productUrl: string;
	keywords: string[];
}

/**
 * A single heatmap cell value and visual intensity metadata.
 */
export interface HeatmapCell {
	label: string;
	value: number;
	bucket: number;
	shade: string;
}

/**
 * A heatmap row keyed by a row label and containing cells for each column.
 */
export interface HeatmapRow {
	label: string;
	cells: HeatmapCell[];
}

/**
 * A complete heatmap matrix shown in the dashboard.
 */
export interface Heatmap {
	title: string;
	columns: string[];
	rows: HeatmapRow[];
}

/**
 * A high-level prediction generated from current meal patterns.
 */
export interface Prediction {
	title: string;
	detail: string;
	confidence: number;
}

/**
 * A model-generated future meal candidate that is not yet announced.
 */
export interface UnannouncedMealPrediction {
	name: string;
	category: string;
	rationale: string;
	predictedPrice: number;
	confidence: number;
	keywords: string[];
}

/**
 * A single menu item returned for a specific day from AVI Dish.
 */
export interface DailyMenuItem {
	mealName: string;
	station: string;
	category: string;
	price?: number;
	tags: string[];
}

/**
 * A day bucket containing menu items available on that date.
 */
export interface DailyMenu {
	date: string;
	items: DailyMenuItem[];
}

/**
 * Aggregated meal occurrence count used to identify duplicates.
 */
export interface MealOccurrence {
	mealName: string;
	occurrenceCount: number;
}

/**
 * API response payload returned by `/api/meals`.
 */
export interface MealAnalyticsResponse {
	portalUrl: string;
	diningUrl: string;
	menuUrl: string;
	retrievedAtUtc: string;
	summary: MealSummary;
	meals: MealItem[];
	heatmaps: Heatmap[];
	predictions: Prediction[];
	unannouncedMealPredictions: UnannouncedMealPrediction[];
	dailyMenus: DailyMenu[];
	mealOccurrences: MealOccurrence[];
}

/**
 * Supported dashboard page filters in the client navigation.
 */
export type DashboardPage = 'summary' | 'predictions' | 'heatmaps' | 'meals' | 'dailyMenus' | 'mealOccurrences' | 'all';

/**
 * Metadata for rendering a dashboard page navigation button.
 */
export type DashboardPageOption = {
	key: DashboardPage;
	label: string;
};
