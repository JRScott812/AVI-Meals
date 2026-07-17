using AVI_Meals.Server.Models;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Derives summary, heatmaps, predictions, and occurrence stats from meal catalogs.
/// </summary>
internal static class MealAnalyticsBuilder
{
	private static readonly string[] ProteinTerms = ["Chicken", "Turkey", "Pork", "Beef", "Tofu", "Veggie", "Falafel", "Egg"];
	private static readonly string[] StyleTerms = ["Bowl", "Wrap", "Skillet", "Pasta", "Salad", "Flatbread", "Tacos", "Sandwich"];
	private static readonly string[] FlavorTerms = ["Garden", "Smoky", "Harvest", "Herb", "Citrus", "Maple", "Southwest", "Mediterranean"];

	public static IReadOnlyList<MealOccurrence> BuildMealOccurrences(
		IReadOnlyList<MealItem> meals,
		IReadOnlyList<DailyMenu> dailyMenus)
	{
		IEnumerable<string> primaryNames = meals
			.Select(meal => meal.Name)
			.Where(name => !string.IsNullOrWhiteSpace(name));
		IEnumerable<string> dailyNames = dailyMenus
			.SelectMany(day => day.Items)
			.Select(item => item.MealName)
			.Where(name => !string.IsNullOrWhiteSpace(name));

		return [.. primaryNames
			.Concat(dailyNames)
			.GroupBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(group => new MealOccurrence(group.First(), group.Count()))
			.OrderByDescending(item => item.OccurrenceCount)
			.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)];
	}

	public static MealSummary BuildSummary(IReadOnlyList<MealItem> meals)
	{
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

	public static IReadOnlyList<Heatmap> BuildHeatmaps(IReadOnlyList<MealItem> meals)
	{
		CateringCategory[] categories = [.. meals
			.Select(meal => meal.Category)
			.Distinct()
			.OrderBy(category => category)];

		string[] priceBands = ["Under $10", "$10-$14.99", "$15-$19.99", "$20+"];
		HeatmapRow[] categoryByPriceRows = [.. categories
			.Select(category => new HeatmapRow(
				MealTaxonomy.FormatCateringCategory(category),
				[.. priceBands.Select(band => new HeatmapCell(
					band,
					meals.Count(meal => meal.Category == category && GetPriceBand(meal.Price) == band),
					0))]))];

		string[] topKeywords = [.. meals
			.SelectMany(meal => meal.Keywords)
			.GroupBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Take(6)
			.Select(group => group.Key)];

		HeatmapRow[] categoryByKeywordRows = [.. categories
			.Select(category => new HeatmapRow(
				MealTaxonomy.FormatCateringCategory(category),
				[.. topKeywords.Select(keyword => new HeatmapCell(
					keyword,
					meals.Count(meal => meal.Category == category
						&& meal.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase)),
					0))]))];

		return
		[
			BuildHeatmap("Category vs price band", priceBands, categoryByPriceRows),
			BuildHeatmap("Category vs common keywords", topKeywords, categoryByKeywordRows)
		];
	}

	public static IReadOnlyList<Prediction> BuildPredictions(IReadOnlyList<MealItem> meals)
	{
		IGrouping<CateringCategory, MealItem> dominantCategory = meals
			.GroupBy(meal => meal.Category)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key)
			.First();

		IGrouping<string, MealItem> dominantBand = meals
			.GroupBy(meal => GetPriceBand(meal.Price), StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.First();

		string[] topKeywords = [.. meals
			.SelectMany(meal => meal.Keywords)
			.GroupBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Take(3)
			.Select(group => $"{group.Key} ({group.Count()})")];

		decimal confidence = decimal.Round(dominantCategory.Count() / (decimal)meals.Count, 2);
		decimal priceConfidence = decimal.Round(dominantBand.Count() / (decimal)meals.Count, 2);
		string categoryLabel = MealTaxonomy.FormatCateringCategory(dominantCategory.Key);

		return
		[
			new Prediction(
				"Most likely menu focus",
				$"{categoryLabel} has the deepest lineup with {dominantCategory.Count()} meals, so similar menus are most likely to emphasize that category.",
				confidence),
			new Prediction(
				"Most likely price range",
				$"{dominantBand.Key} contains {dominantBand.Count()} listed meals, which makes it the strongest pricing cluster in the current menu.",
				priceConfidence),
			new Prediction(
				"Most likely recurring meal themes",
				$"The most repeated keywords are {string.Join(", ", topKeywords)}, so future additions are most likely to reuse those themes.",
				topKeywords.Length == 0 ? 0m : 0.65m)
		];
	}

	public static IReadOnlyList<UnannouncedMealPrediction> BuildUnannouncedMealPredictions(IReadOnlyList<MealItem> meals)
	{
		CateringCategory[] dominantCategories = [.. meals
			.GroupBy(meal => meal.Category)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key)
			.Take(3)
			.Select(group => group.Key)];

		if (dominantCategories.Length == 0)
		{
			return [];
		}

		Dictionary<CateringCategory, decimal> categoryAveragePrices = meals
			.GroupBy(meal => meal.Category)
			.ToDictionary(
				group => group.Key,
				group => decimal.Round(group.Average(meal => meal.Price), 2));

		string[] topKeywords = [.. meals
			.SelectMany(meal => meal.Keywords)
			.GroupBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Take(10)
			.Select(group => group.Key)];

		HashSet<string> existingNames = meals
			.Select(meal => meal.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		List<UnannouncedMealPrediction> predictions = [];
		HashSet<string> generatedNames = new(StringComparer.OrdinalIgnoreCase);

		for (int index = 0; index < dominantCategories.Length; index++)
		{
			CateringCategory category = dominantCategories[index];
			string protein = ProteinTerms[index % ProteinTerms.Length];
			string style = StyleTerms[(index + 2) % StyleTerms.Length];
			string flavor = FlavorTerms[(index + 4) % FlavorTerms.Length];
			string keyword = topKeywords.Length == 0 ? "seasonal" : topKeywords[index * 2 % topKeywords.Length];
			string candidateName = $"{flavor} {protein} {style}";

			if (existingNames.Contains(candidateName) || generatedNames.Contains(candidateName))
			{
				candidateName = $"{flavor} {protein} {style} Special";
			}

			_ = generatedNames.Add(candidateName);

			decimal baseline = categoryAveragePrices.TryGetValue(category, out decimal averagePrice)
				? averagePrice
				: meals.Average(meal => meal.Price);
			decimal predictedPrice = decimal.Round(baseline + ((index - 1) * 0.75m), 2);
			if (predictedPrice < 5m)
			{
				predictedPrice = 5m;
			}

			decimal confidence = decimal.Round(Math.Max(0.45m, 0.78m - (index * 0.1m)), 2);
			string rationale = $"This category appears frequently, and keywords like '{keyword}' recur across announced meals, suggesting a similar upcoming item.";
			string[] predictionKeywords =
			[
				flavor.ToLowerInvariant(),
				protein.ToLowerInvariant(),
				style.ToLowerInvariant(),
				keyword.ToLowerInvariant()
			];

			predictions.Add(new UnannouncedMealPrediction(
				candidateName,
				category,
				rationale,
				predictedPrice,
				confidence,
				predictionKeywords));
		}

		return predictions;
	}

	private static Heatmap BuildHeatmap(string title, IReadOnlyList<string> columns, IReadOnlyList<HeatmapRow> rows)
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

		return new Heatmap(title, columns, mappedRows);
	}

	private static string GetPriceBand(decimal price)
	{
		return price switch
		{
			< 10m => "Under $10",
			< 15m => "$10-$14.99",
			< 20m => "$15-$19.99",
			_ => "$20+"
		};
	}
}
