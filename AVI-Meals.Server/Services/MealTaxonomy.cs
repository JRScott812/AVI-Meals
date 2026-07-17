using System.Text.RegularExpressions;

using AVI_Meals.Server.Models;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Maps upstream free-text meal taxonomy into enums and derived keyword lists.
/// </summary>
internal static partial class MealTaxonomy
{
	public static CateringCategory ParseCateringCategory(string? raw)
	{
		string normalized = NormalizeKey(raw);
		return normalized switch
		{
			"APPETIZER DISPLAYS" or "APPETIZER DISPLAY" => CateringCategory.AppetizerDisplays,
			"BOXED LUNCH" or "BOXED LUNCHES" => CateringCategory.BoxedLunch,
			"BREAKFAST" => CateringCategory.Breakfast,
			"HOT BUFFETS" or "HOT BUFFET" => CateringCategory.HotBuffets,
			"SANDWICH AND SALAD BUFFETS" or "SANDWICH AND SALAD BUFFET"
				or "SANDWICH & SALAD BUFFETS" => CateringCategory.SandwichAndSaladBuffets,
			_ => CateringCategory.Other
		};
	}

	public static string FormatCateringCategory(CateringCategory category) => category switch
	{
		CateringCategory.AppetizerDisplays => "Appetizer Displays",
		CateringCategory.BoxedLunch => "Boxed Lunch",
		CateringCategory.Breakfast => "Breakfast",
		CateringCategory.HotBuffets => "Hot Buffets",
		CateringCategory.SandwichAndSaladBuffets => "Sandwich and Salad Buffets",
		_ => "Other"
	};

	public static MealType ParseMealType(string? raw)
	{
		string normalized = NormalizeKey(raw);
		if (normalized.Length == 0)
		{
			return MealType.Unknown;
		}

		if (normalized.Contains("BRUNCH", StringComparison.Ordinal))
		{
			return MealType.Brunch;
		}

		if (normalized.Contains("BREAKFAST", StringComparison.Ordinal))
		{
			return MealType.Breakfast;
		}

		if (normalized.Contains("DINNER", StringComparison.Ordinal)
			|| normalized.Contains("SUPPER", StringComparison.Ordinal))
		{
			return MealType.Dinner;
		}

		if (normalized.Contains("LUNCH", StringComparison.Ordinal)
			|| normalized.Contains("MIDDAY", StringComparison.Ordinal))
		{
			return MealType.Lunch;
		}

		return MealType.Unknown;
	}

	public static DiningStation ParseStation(string? raw)
	{
		string normalized = NormalizeKey(raw);
		if (normalized.Length == 0)
		{
			return DiningStation.General;
		}

		normalized = StripLeadingArchivePrefix(normalized);
		string stationKey = StripTrailingMealPeriod(normalized);

		return stationKey switch
		{
			"NUTRIBAR" or "NUTRI BAR" or "NUTRI SALADS" => DiningStation.NutriBar,
			"TRATTORIA" => DiningStation.Trattoria,
			"CLARITY" => DiningStation.Clarity,
			"HOMESTYLE" => DiningStation.Homestyle,
			"HOMESTEAD" => DiningStation.Homestead,
			"PASTAS" or "PASTA" => DiningStation.Pastas,
			"GRILL" or "1846 GRILL" => stationKey.StartsWith("1846", StringComparison.Ordinal)
				? DiningStation.Grill1846
				: DiningStation.Grill,
			"GRILL AND SPECIALS" or "GRILL & SPECIALS" => DiningStation.GrillAndSpecials,
			"GRILL TAKE OVER" or "GRILL TAKEOVER" => DiningStation.GrillTakeOver,
			"DELI" => DiningStation.Deli,
			"DELI AND FEATURES" or "DELI & FEATURES" => DiningStation.DeliAndFeatures,
			"SOUPS" or "SOUP" => DiningStation.Soups,
			"YOGURT BAR" or "YOGURTBAR" => DiningStation.YogurtBar,
			"ROOTS" => DiningStation.Roots,
			"TOP AND TOAST" or "TOP & TOAST" => DiningStation.TopAndToast,
			"HOT CEREALS" or "HOT CEREAL" => DiningStation.HotCereals,
			"HOT BREAKFAST SPECIALS" => DiningStation.HotBreakfastSpecials,
			"CARVERY" => DiningStation.Carvery,
			"TAILGATE" => DiningStation.Tailgate,
			"TAYLOR FOOD TRUCK" or "FOOD TRUCK" => DiningStation.FoodTruck,
			"TAKE OVER TOPPING" or "TAKEOVER TOPPING" => DiningStation.TakeOverTopping,
			"MAIN LINE" or "MAINLINE" or "GENERAL" => DiningStation.MainLine,
			"BRUNCH" or "BREAKFAST" or "LUNCH" or "DINNER" => DiningStation.General,
			_ when stationKey.Contains("1846", StringComparison.Ordinal) => DiningStation.Grill1846,
			_ when stationKey.Contains("TRATTORIA", StringComparison.Ordinal) => DiningStation.Trattoria,
			_ when stationKey.Contains("CLARITY", StringComparison.Ordinal) => DiningStation.Clarity,
			_ when stationKey.Contains("HOMESTYLE", StringComparison.Ordinal) => DiningStation.Homestyle,
			_ when stationKey.Contains("GRILL", StringComparison.Ordinal)
				&& stationKey.Contains("SPECIAL", StringComparison.Ordinal) => DiningStation.GrillAndSpecials,
			_ when stationKey.Contains("GRILL", StringComparison.Ordinal) => DiningStation.Grill,
			_ when stationKey.Contains("DELI", StringComparison.Ordinal) => DiningStation.Deli,
			_ when stationKey.Contains("SOUP", StringComparison.Ordinal) => DiningStation.Soups,
			_ when stationKey.Contains("YOGURT", StringComparison.Ordinal) => DiningStation.YogurtBar,
			_ when stationKey.Contains("ROOTS", StringComparison.Ordinal) => DiningStation.Roots,
			_ when stationKey.Contains("NUTRI", StringComparison.Ordinal) => DiningStation.NutriBar,
			_ => DiningStation.Other
		};
	}

	public static string FormatStation(DiningStation station) => station switch
	{
		DiningStation.NutriBar => "NutriBar",
		DiningStation.GrillAndSpecials => "Grill & Specials",
		DiningStation.GrillTakeOver => "Grill Take Over",
		DiningStation.DeliAndFeatures => "Deli & Features",
		DiningStation.YogurtBar => "Yogurt Bar",
		DiningStation.TopAndToast => "Top & Toast",
		DiningStation.HotCereals => "Hot Cereals",
		DiningStation.HotBreakfastSpecials => "Hot Breakfast Specials",
		DiningStation.FoodTruck => "Taylor Food Truck",
		DiningStation.TakeOverTopping => "Take Over Topping",
		DiningStation.Grill1846 => "1846 Grill",
		DiningStation.MainLine => "Main Line",
		DiningStation.General => "General",
		DiningStation.Other => "Other",
		_ => station.ToString()
	};

	public static IReadOnlyList<string> ExtractKeywords(string name, string description) =>
		MealKeywords.Extract(name, description);

	private static string NormalizeKey(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}

		string decoded = raw.Trim();
		decoded = WhitespaceRegex().Replace(decoded, " ");
		return decoded.ToUpperInvariant();
	}

	private static string StripLeadingArchivePrefix(string normalized)
	{
		string trimmed = normalized;
		while (trimmed.StartsWith("ZZ", StringComparison.Ordinal))
		{
			trimmed = trimmed[2..].TrimStart();
		}

		return trimmed;
	}

	private static string StripTrailingMealPeriod(string normalized)
	{
		string[] suffixes =
		[
			" LIGHT LUNCH",
			" BREAKFAST",
			" BRUNCH",
			" LUNCH",
			" DINNER",
			" SUPPER"
		];

		foreach (string suffix in suffixes)
		{
			if (normalized.EndsWith(suffix, StringComparison.Ordinal))
			{
				return normalized[..^suffix.Length].TrimEnd();
			}
		}

		return normalized;
	}

	[GeneratedRegex("\\s+")]
	private static partial Regex WhitespaceRegex();
}
