using System.Text.Json.Serialization;

namespace AVI_Meals.Server.Models;

/// <summary>
/// Dining meal period (breakfast / lunch / dinner, etc.).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MealType
{
	Unknown = 0,
	Breakfast,
	Brunch,
	Lunch,
	Dinner
}

/// <summary>
/// Normalized Hodson / campus dining station.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiningStation
{
	Other = 0,
	General,
	MainLine,
	NutriBar,
	Trattoria,
	Clarity,
	Homestyle,
	Homestead,
	Pastas,
	Grill,
	GrillAndSpecials,
	GrillTakeOver,
	Deli,
	DeliAndFeatures,
	Soups,
	YogurtBar,
	Roots,
	TopAndToast,
	HotCereals,
	HotBreakfastSpecials,
	Carvery,
	Tailgate,
	FoodTruck,
	TakeOverTopping,
	Grill1846
}

/// <summary>
/// CaterTrax catering catalog category.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CateringCategory
{
	Other = 0,
	AppetizerDisplays,
	BoxedLunch,
	Breakfast,
	HotBuffets,
	SandwichAndSaladBuffets
}
