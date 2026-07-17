using System.Globalization;

using AVI_Meals.Server.Models;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Derives summary, heatmaps, predictions, and occurrence stats from Hodson menus and catering catalogs.
/// </summary>
internal static class MealAnalyticsBuilder
{
	private static readonly MealType[] MealTypeColumns =
	[
		MealType.Breakfast,
		MealType.Brunch,
		MealType.Lunch,
		MealType.Dinner
	];

	private static readonly DayOfWeek[] WeekdayColumns =
	[
		DayOfWeek.Monday,
		DayOfWeek.Tuesday,
		DayOfWeek.Wednesday,
		DayOfWeek.Thursday,
		DayOfWeek.Friday,
		DayOfWeek.Saturday,
		DayOfWeek.Sunday
	];

	public static IReadOnlyList<MealOccurrence> BuildMealOccurrences(
		IReadOnlyList<MealItem> meals,
		IReadOnlyList<DailyMenu> dailyMenus)
	{
		List<MealOccurrence> fromHistory = [.. dailyMenus
			.SelectMany(day => day.Items.Select(item => (day.Date, Name: item.MealName.Trim())))
			.Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
			.GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
			.Select(group => new MealOccurrence(
				group.First().Name,
				group.Select(entry => entry.Date).Distinct().Count()))];

		if (fromHistory.Count > 0)
		{
			return [.. fromHistory
				.OrderByDescending(item => item.OccurrenceCount)
				.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)];
		}

		return [.. meals
			.Select(meal => meal.Name)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.GroupBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(group => new MealOccurrence(group.First(), group.Count()))
			.OrderByDescending(item => item.OccurrenceCount)
			.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)];
	}

	public static MealSummary BuildSummary(IReadOnlyList<MealItem> meals)
	{
		if (meals.Count == 0)
		{
			return new MealSummary(0, 0, 0m, 0m, 0m, []);
		}

		CateringCategory[] categories = [.. meals
			.Select(meal => meal.Category)
			.Distinct()
			.OrderBy(category => category)];

		return new MealSummary(
			meals.Count,
			categories.Length,
			meals.Min(meal => meal.Price),
			meals.Max(meal => meal.Price),
			decimal.Round(meals.Average(meal => meal.Price), 2),
			categories);
	}

	public static IReadOnlyList<Heatmap> BuildHeatmaps(
		IReadOnlyList<MealItem> meals,
		IReadOnlyList<DailyMenu> dailyMenus)
	{
		DailyMenuItem[] items = [.. dailyMenus.SelectMany(day => day.Items)];
		if (items.Length == 0)
		{
			return BuildCateringFallbackHeatmaps(meals);
		}

		string[] mealTypeLabels = [.. MealTypeColumns.Select(FormatMealType)];
		DiningStation[] topStations = [.. items
			.GroupBy(item => item.Station)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key)
			.Take(10)
			.Select(group => group.Key)];

		HeatmapRow[] stationByMealTypeRows = [.. topStations.Select(station => new HeatmapRow(
			MealTaxonomy.FormatStation(station),
			[.. MealTypeColumns.Select(mealType => new HeatmapCell(
				FormatMealType(mealType),
				items.Count(item => item.Station == station && item.MealType == mealType),
				0))]))];

		string[] weekdayLabels = [.. WeekdayColumns.Select(FormatWeekday)];
		HeatmapRow[] stationByWeekdayRows = [.. topStations.Select(station => new HeatmapRow(
			MealTaxonomy.FormatStation(station),
			[.. WeekdayColumns.Select(weekday => new HeatmapCell(
				FormatWeekday(weekday),
				dailyMenus.Sum(day => day.Date.DayOfWeek == weekday
					? day.Items.Count(item => item.Station == station)
					: 0),
				0))]))];

		string[] topCategories = [.. items
			.Where(item => !string.IsNullOrWhiteSpace(item.Category))
			.GroupBy(item => item.Category.Trim(), StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Take(10)
			.Select(group => group.First().Category.Trim())];

		HeatmapRow[] categoryByMealTypeRows = [.. topCategories.Select(category => new HeatmapRow(
			category,
			[.. MealTypeColumns.Select(mealType => new HeatmapCell(
				FormatMealType(mealType),
				items.Count(item => item.MealType == mealType
					&& string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase)),
				0))]))];

		List<Heatmap> heatmaps =
		[
			BuildHeatmap("Station vs meal type", "Station", mealTypeLabels, stationByMealTypeRows),
			BuildHeatmap("Station vs weekday", "Station", weekdayLabels, stationByWeekdayRows)
		];

		if (topCategories.Length > 0)
		{
			heatmaps.Add(BuildHeatmap("Dish category vs meal type", "Category", mealTypeLabels, categoryByMealTypeRows));
		}

		return heatmaps;
	}

	public static IReadOnlyList<Prediction> BuildPredictions(
		IReadOnlyList<MealItem> meals,
		IReadOnlyList<DailyMenu> dailyMenus)
	{
		DailyMenuItem[] items = [.. dailyMenus.SelectMany(day => day.Items)];
		if (items.Length == 0)
		{
			return BuildCateringFallbackPredictions(meals);
		}

		int dayCount = Math.Max(1, dailyMenus.Count);
		IGrouping<DiningStation, DailyMenuItem> topStation = items
			.GroupBy(item => item.Station)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key)
			.First();

		IGrouping<MealType, DailyMenuItem> topMealType = items
			.Where(item => item.MealType != MealType.Unknown)
			.GroupBy(item => item.MealType)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key)
			.DefaultIfEmpty(items.GroupBy(item => item.MealType).First())
			.First();

		var busiestWeekday = dailyMenus
			.GroupBy(day => day.Date.DayOfWeek)
			.Select(group => new
			{
				Day = group.Key,
				ItemCount = group.Sum(day => day.Items.Count),
				DayCount = group.Count()
			})
			.OrderByDescending(entry => entry.ItemCount)
			.ThenBy(entry => entry.Day)
			.First();

		MealOccurrence[] recurring = [.. BuildMealOccurrences([], dailyMenus).Take(5)];
		string recurringSummary = recurring.Length == 0
			? "No strong repeat dishes yet"
			: string.Join(", ", recurring.Select(item => $"{item.MealName} ({item.OccurrenceCount} days)"));

		decimal stationConfidence = decimal.Round(topStation.Count() / (decimal)items.Length, 2);
		decimal mealTypeConfidence = decimal.Round(topMealType.Count() / (decimal)items.Length, 2);
		decimal weekdayConfidence = decimal.Round(
			busiestWeekday.ItemCount / (decimal)Math.Max(1, items.Length),
			2);
		decimal recurrenceConfidence = recurring.Length == 0
			? 0m
			: decimal.Round(Math.Min(0.9m, recurring[0].OccurrenceCount / (decimal)dayCount), 2);

		return
		[
			new Prediction(
				"Most active Hodson station",
				$"{MealTaxonomy.FormatStation(topStation.Key)} accounts for {topStation.Count()} of {items.Length} plated items across {dayCount} days, so it is the strongest station signal in the dining hall history.",
				stationConfidence),
			new Prediction(
				"Dominant meal period",
				$"{FormatMealType(topMealType.Key)} carries {topMealType.Count()} items in the stored history, making it the period most likely to keep the broadest lineup.",
				mealTypeConfidence),
			new Prediction(
				"Busiest weekday pattern",
				$"{FormatWeekday(busiestWeekday.Day)} averages the heaviest menus ({busiestWeekday.ItemCount} items over {busiestWeekday.DayCount} sampled day(s)), so expect denser station coverage then.",
				weekdayConfidence),
			new Prediction(
				"Dishes most likely to return",
				$"The most persistent Hodson dishes are {recurringSummary}. Items that already rotate through many days are the best candidates to reappear.",
				recurrenceConfidence)
		];
	}

	public static IReadOnlyList<UnannouncedMealPrediction> BuildUnannouncedMealPredictions(
		IReadOnlyList<MealItem> meals,
		IReadOnlyList<DailyMenu> dailyMenus)
	{
		DailyMenuItem[] items = [.. dailyMenus.SelectMany(day => day.Items)];
		if (items.Length == 0)
		{
			return BuildCateringFallbackUnannounced(meals);
		}

		var stationProfiles = items
			.GroupBy(item => item.Station)
			.OrderByDescending(group => group.Count())
			.Take(4)
			.Select(group =>
			{
				MealType mealType = group
					.Where(item => item.MealType != MealType.Unknown)
					.GroupBy(item => item.MealType)
					.OrderByDescending(mealGroup => mealGroup.Count())
					.Select(mealGroup => mealGroup.Key)
					.DefaultIfEmpty(MealType.Lunch)
					.First();
				string category = group
					.Where(item => !string.IsNullOrWhiteSpace(item.Category))
					.GroupBy(item => item.Category.Trim(), StringComparer.OrdinalIgnoreCase)
					.OrderByDescending(categoryGroup => categoryGroup.Count())
					.Select(categoryGroup => categoryGroup.First().Category.Trim())
					.DefaultIfEmpty("Homestyle")
					.First();
				string[] tokens = [.. group
					.SelectMany(item => MealKeywords.Extract(item.MealName, string.Empty))
					.GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
					.OrderByDescending(tokenGroup => tokenGroup.Count())
					.Take(6)
					.Select(tokenGroup => tokenGroup.Key)];
				return new
				{
					Station = group.Key,
					MealType = mealType,
					Category = category,
					Tokens = tokens,
					Count = group.Count()
				};
			})
			.Where(profile => profile.Tokens.Length >= 2)
			.ToArray();

		if (stationProfiles.Length == 0)
		{
			return [];
		}

		HashSet<string> existingNames = items
			.Select(item => item.MealName.Trim())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		List<UnannouncedMealPrediction> predictions = [];
		HashSet<string> generatedNames = new(StringComparer.OrdinalIgnoreCase);
		string[] connectors = ["Garden", "Roasted", "Crispy", "Herb", "Seasonal", "Smoky"];

		for (int index = 0; index < stationProfiles.Length; index++)
		{
			var profile = stationProfiles[index];
			string lead = connectors[index % connectors.Length];
			string body = string.Join(' ', profile.Tokens.Take(2).Select(ToTitleCase));
			string candidateName = $"{lead} {body}".Trim();
			if (existingNames.Contains(candidateName) || generatedNames.Contains(candidateName))
			{
				candidateName = $"{lead} {body} Bowl";
			}

			if (existingNames.Contains(candidateName) || !generatedNames.Add(candidateName))
			{
				continue;
			}

			decimal confidence = decimal.Round(
				Math.Clamp(0.42m + (profile.Count / (decimal)Math.Max(20, items.Length)), 0.45m, 0.82m),
				2);
			string rationale =
				$"{MealTaxonomy.FormatStation(profile.Station)} repeatedly serves {profile.Category.ToLowerInvariant()} during {FormatMealType(profile.MealType).ToLowerInvariant()}, and tokens like '{profile.Tokens[0]}' keep showing up—so a nearby unannounced special is plausible.";

			predictions.Add(new UnannouncedMealPrediction(
				candidateName,
				profile.Station,
				profile.MealType,
				profile.Category,
				rationale,
				confidence,
				profile.Tokens.Take(4).ToArray()));
		}

		return predictions;
	}

	private static IReadOnlyList<Heatmap> BuildCateringFallbackHeatmaps(IReadOnlyList<MealItem> meals)
	{
		if (meals.Count == 0)
		{
			return [];
		}

		CateringCategory[] categories = [.. meals.Select(meal => meal.Category).Distinct().OrderBy(category => category)];
		string[] priceBands = ["Under $10", "$10-$14.99", "$15-$19.99", "$20+"];
		HeatmapRow[] rows = [.. categories.Select(category => new HeatmapRow(
			MealTaxonomy.FormatCateringCategory(category),
			[.. priceBands.Select(band => new HeatmapCell(
				band,
				meals.Count(meal => meal.Category == category && GetPriceBand(meal.Price) == band),
				0))]))];

		return [BuildHeatmap("Catering category vs price band", "Category", priceBands, rows)];
	}

	private static IReadOnlyList<Prediction> BuildCateringFallbackPredictions(IReadOnlyList<MealItem> meals)
	{
		if (meals.Count == 0)
		{
			return [];
		}

		IGrouping<CateringCategory, MealItem> dominantCategory = meals
			.GroupBy(meal => meal.Category)
			.OrderByDescending(group => group.Count())
			.First();

		return
		[
			new Prediction(
				"Catering catalog focus",
				$"{MealTaxonomy.FormatCateringCategory(dominantCategory.Key)} leads the public CaterTrax catalog with {dominantCategory.Count()} items. Hodson daily history is unavailable for richer residential predictions.",
				decimal.Round(dominantCategory.Count() / (decimal)meals.Count, 2))
		];
	}

	private static IReadOnlyList<UnannouncedMealPrediction> BuildCateringFallbackUnannounced(IReadOnlyList<MealItem> meals)
	{
		if (meals.Count == 0)
		{
			return [];
		}

		string[] keywords = [.. meals
			.SelectMany(meal => meal.Keywords)
			.GroupBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.Take(4)
			.Select(group => group.Key)];

		string name = keywords.Length >= 2
			? $"{ToTitleCase(keywords[0])} {ToTitleCase(keywords[1])} Special"
			: "Seasonal Campus Special";

		return
		[
			new UnannouncedMealPrediction(
				name,
				DiningStation.Homestyle,
				MealType.Lunch,
				"Catering",
				"Generated from CaterTrax keyword patterns because Hodson daily history was empty.",
				0.4m,
				keywords)
		];
	}

	private static Heatmap BuildHeatmap(
		string title,
		string rowHeader,
		IReadOnlyList<string> columns,
		IReadOnlyList<HeatmapRow> rows)
	{
		int max = rows.SelectMany(row => row.Cells).Select(cell => cell.Value).DefaultIfEmpty(0).Max();
		HeatmapRow[] mappedRows = [.. rows
			.Select(row => new HeatmapRow(
				row.Label,
				[.. row.Cells.Select(cell =>
				{
					int bucket = max == 0 ? 0 : (int)Math.Ceiling(cell.Value / (double)max * 4);
					return cell with { Bucket = bucket };
				})]))];

		return new Heatmap(title, rowHeader, columns, mappedRows);
	}

	private static string GetPriceBand(decimal price) => price switch
	{
		< 10m => "Under $10",
		< 15m => "$10-$14.99",
		< 20m => "$15-$19.99",
		_ => "$20+"
	};

	private static string FormatMealType(MealType mealType) => mealType switch
	{
		MealType.Unknown => "Unknown",
		_ => mealType.ToString()
	};

	private static string FormatWeekday(DayOfWeek day) => day switch
	{
		DayOfWeek.Monday => "Mon",
		DayOfWeek.Tuesday => "Tue",
		DayOfWeek.Wednesday => "Wed",
		DayOfWeek.Thursday => "Thu",
		DayOfWeek.Friday => "Fri",
		DayOfWeek.Saturday => "Sat",
		DayOfWeek.Sunday => "Sun",
		_ => day.ToString()
	};

	private static string ToTitleCase(string value) =>
		CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
}
