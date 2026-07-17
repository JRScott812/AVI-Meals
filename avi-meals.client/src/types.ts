export interface MealSummary {
	mealCount: number;
	categoryCount: number;
	lowestPrice: number;
	highestPrice: number;
	averagePrice: number;
	categoryNames: string[];
}

export interface MealItem {
	category: string;
	name: string;
	description: string;
	price: number;
	priceLabel: string;
	productUrl: string;
	keywords: string[];
}

export interface HeatmapCell {
	label: string;
	value: number;
	bucket: number;
}

export interface HeatmapRow {
	label: string;
	cells: HeatmapCell[];
}

export interface Heatmap {
	title: string;
	columns: string[];
	rows: HeatmapRow[];
}

export interface Prediction {
	title: string;
	detail: string;
	confidence: number;
}

export interface UnannouncedMealPrediction {
	name: string;
	category: string;
	rationale: string;
	predictedPrice: number;
	confidence: number;
	keywords: string[];
}

export interface DailyMenuItem {
	mealName: string;
	station: string;
	category: string;
	price?: number;
	tags: string[];
}

export interface DailyMenu {
	date: string;
	items: DailyMenuItem[];
}

export interface MealOccurrence {
	mealName: string;
	occurrenceCount: number;
}

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

export type DashboardPage = 'summary' | 'predictions' | 'heatmaps' | 'meals' | 'dailyMenus' | 'mealOccurrences' | 'all';

export type DashboardPageOption = {
	key: DashboardPage;
	label: string;
};
