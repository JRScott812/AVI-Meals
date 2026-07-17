export type MealType = 'Unknown' | 'Breakfast' | 'Brunch' | 'Lunch' | 'Dinner';

export type DiningStation =
	| 'Other'
	| 'General'
	| 'MainLine'
	| 'NutriBar'
	| 'Trattoria'
	| 'Clarity'
	| 'Homestyle'
	| 'Homestead'
	| 'Pastas'
	| 'Grill'
	| 'GrillAndSpecials'
	| 'GrillTakeOver'
	| 'Deli'
	| 'DeliAndFeatures'
	| 'Soups'
	| 'YogurtBar'
	| 'Roots'
	| 'TopAndToast'
	| 'HotCereals'
	| 'HotBreakfastSpecials'
	| 'Carvery'
	| 'Tailgate'
	| 'FoodTruck'
	| 'TakeOverTopping'
	| 'Grill1846';

export type CateringCategory =
	| 'Other'
	| 'AppetizerDisplays'
	| 'BoxedLunch'
	| 'Breakfast'
	| 'HotBuffets'
	| 'SandwichAndSaladBuffets';

export interface MealSummary {
	mealCount: number;
	categoryCount: number;
	lowestPrice: number;
	highestPrice: number;
	averagePrice: number;
	categories: CateringCategory[];
}

export interface MealItem {
	category: CateringCategory;
	name: string;
	description: string;
	price: number;
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
	category: CateringCategory;
	rationale: string;
	predictedPrice: number;
	confidence: number;
	keywords: string[];
}

export interface DailyMenuItem {
	mealName: string;
	station: DiningStation;
	mealType: MealType;
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
