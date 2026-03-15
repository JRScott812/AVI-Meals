using AVI_Meals.Server.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace AVI_Meals.Server.Services;

public sealed partial class MealAnalyticsService(HttpClient httpClient, IMemoryCache cache)
{
	private const string CacheKey = "meal-analytics";
	private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
	private const string PortalUrl = "https://connecttaylor.atriumcampus.com/index.php";

	private static readonly string[] ProteinTerms = ["Chicken", "Turkey", "Pork", "Beef", "Tofu", "Veggie", "Falafel", "Egg"];
	private static readonly string[] StyleTerms = ["Bowl", "Wrap", "Skillet", "Pasta", "Salad", "Flatbread", "Tacos", "Sandwich"];
	private static readonly string[] FlavorTerms = ["Garden", "Smoky", "Harvest", "Herb", "Citrus", "Maple", "Southwest", "Mediterranean"];

	/// <summary>
	/// Gets the latest meal analytics snapshot from the public dining sources.
	/// </summary>
	public async Task<MealAnalyticsResponse> GetAnalyticsAsync(CancellationToken cancellationToken)
	{
		var result = await cache.GetOrCreateAsync(CacheKey, async entry =>
		{
			entry.AbsoluteExpirationRelativeToNow = CacheDuration;
			return await BuildAnalyticsAsync(cancellationToken).ConfigureAwait(false);
		}).ConfigureAwait(false);

		return result ?? throw new InvalidOperationException("Unable to load meal analytics.");
	}

	private async Task<MealAnalyticsResponse> BuildAnalyticsAsync(CancellationToken cancellationToken)
	{
		string portalHtml = await GetHtmlAsync(PortalUrl, cancellationToken).ConfigureAwait(false);
		string diningUrl = ExtractFirstAbsoluteUrl(portalHtml, "aviserves.com")
			?? throw new InvalidOperationException("The Taylor dining page link could not be found.");

		string diningHtml = await GetHtmlAsync(diningUrl, cancellationToken).ConfigureAwait(false);
		string caterTraxBaseUrl = ExtractFirstAbsoluteUrl(diningHtml, "catertrax.com")
			?? throw new InvalidOperationException("The catering menu link could not be found.");

		string menuUrl = new Uri(new Uri(caterTraxBaseUrl), "menugrid.asp?mode=aff").ToString();
		string menuHtml = await GetHtmlAsync(menuUrl, cancellationToken).ConfigureAwait(false);
		List<CategoryLink> categories = ExtractCategories(menuHtml, caterTraxBaseUrl);

		if (categories.Count == 0)
		{
			throw new InvalidOperationException("No meal categories were found in the public menu.");
		}

		List<MealItem> meals = [];
		foreach (CategoryLink category in categories)
		{
			cancellationToken.ThrowIfCancellationRequested();

			string categoryHtml = await GetHtmlAsync(category.Url, cancellationToken).ConfigureAwait(false);
			meals.AddRange(ExtractMeals(category.Name, categoryHtml, caterTraxBaseUrl));
		}

		if (meals.Count == 0)
		{
			throw new InvalidOperationException("No meals were found in the public menu.");
		}

		MealItem[] orderedMeals = [.. meals
			.OrderBy(meal => meal.Category, StringComparer.OrdinalIgnoreCase)
			.ThenBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)];

		return new MealAnalyticsResponse(
			PortalUrl,
			diningUrl,
			menuUrl,
			DateTimeOffset.UtcNow,
			BuildSummary(orderedMeals),
			orderedMeals,
			BuildHeatmaps(orderedMeals),
			BuildPredictions(orderedMeals),
			BuildUnannouncedMealPredictions(orderedMeals));
	}

	private async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
	}

	private static List<CategoryLink> ExtractCategories(string html, string baseUrl)
	{
		MatchCollection matches = CategoryRegex().Matches(html);
		List<CategoryLink> categories = new(matches.Count);

		foreach (Match match in matches)
		{
			string href = match.Groups["href"].Value;
			string name = NormalizeText(match.Groups["name"].Value);
			if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			categories.Add(new CategoryLink(name, MakeAbsoluteUrl(baseUrl, href)));
		}

		return categories;
	}

	private static IEnumerable<MealItem> ExtractMeals(string categoryName, string html, string baseUrl)
	{
		foreach (Match match in ProductRegex().Matches(html))
		{
			string name = NormalizeText(match.Groups["name"].Value);
			string description = NormalizeText(StripHtml(match.Groups["description"].Value));
			string priceText = NormalizeText(match.Groups["price"].Value);
			string productHref = match.Groups["href"].Value;

			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(priceText))
			{
				continue;
			}

			if (!TryParsePrice(priceText, out decimal price))
			{
				continue;
			}

			yield return new MealItem(
				categoryName,
				name,
				description,
				price,
				priceText,
				MakeAbsoluteUrl(baseUrl, productHref),
				ExtractKeywords(name, description));
		}
	}

	private static MealSummary BuildSummary(MealItem[] meals)
	{
		string[] categories = [.. meals
			.Select(meal => meal.Category)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

		return new MealSummary(
			meals.Length,
			categories.Length,
			meals.Min(meal => meal.Price),
			meals.Max(meal => meal.Price),
			decimal.Round(meals.Average(meal => meal.Price), 2),
			categories);
	}

	private static IReadOnlyList<Heatmap> BuildHeatmaps(IReadOnlyList<MealItem> meals)
	{
		string[] categoryNames = [.. meals
			.Select(meal => meal.Category)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

		string[] priceBands = ["Under $10", "$10-$14.99", "$15-$19.99", "$20+"];
		HeatmapRow[] categoryByPriceRows = [.. categoryNames
			.Select(category => new HeatmapRow(
				category,
				[.. priceBands.Select(band => new HeatmapCell(
					band,
					meals.Count(meal => string.Equals(meal.Category, category, StringComparison.OrdinalIgnoreCase) && GetPriceBand(meal.Price) == band),
					0,
					string.Empty))]))];

		string[] topKeywords = [.. meals
			.SelectMany(meal => meal.Keywords)
			.GroupBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Take(6)
			.Select(group => group.Key)];

		HeatmapRow[] categoryByKeywordRows = [.. categoryNames
			.Select(category => new HeatmapRow(
				category,
				[.. topKeywords.Select(keyword => new HeatmapCell(
					keyword,
					meals.Count(meal => string.Equals(meal.Category, category, StringComparison.OrdinalIgnoreCase) && meal.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase)),
					0,
					string.Empty))]))];

		return
		[
			BuildHeatmap("Category vs price band", priceBands, categoryByPriceRows),
			BuildHeatmap("Category vs common keywords", topKeywords, categoryByKeywordRows)
		];
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
					return cell with
					{
						Bucket = bucket,
						Shade = bucket switch
						{
							0 => "·",
							1 => "░",
							2 => "▒",
							3 => "▓",
							_ => "█"
						}
					};
				})]))];

		return new Heatmap(title, columns, mappedRows);
	}

	private static IReadOnlyList<Prediction> BuildPredictions(MealItem[] meals)
	{
		IGrouping<string, MealItem> dominantCategory = meals
			.GroupBy(meal => meal.Category, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
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

		float confidence = float.Round(dominantCategory.Count() / (float)meals.Length, 2);
		float priceConfidence = float.Round(dominantBand.Count() / (float)meals.Length, 2);

		return
		[
			new Prediction(
				"Most likely menu focus",
				$"{dominantCategory.Key} has the deepest lineup with {dominantCategory.Count()} meals, so similar menus are most likely to emphasize that category.",
				confidence),
			new Prediction(
				"Most likely price range",
				$"{dominantBand.Key} contains {dominantBand.Count()} listed meals, which makes it the strongest pricing cluster in the current menu.",
				priceConfidence),
			new Prediction(
				"Most likely recurring meal themes",
				$"The most repeated keywords are {string.Join(", ", topKeywords)}, so future additions are most likely to reuse those themes.",
				topKeywords.Length == 0 ? 0 : 0.65f)
		];
	}

	private static IReadOnlyList<UnannouncedMealPrediction> BuildUnannouncedMealPredictions(IReadOnlyList<MealItem> meals)
	{
		string[] dominantCategories = [.. meals
			.GroupBy(meal => meal.Category, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Take(3)
			.Select(group => group.Key)];

		if (dominantCategories.Length == 0)
		{
			return [];
		}

		Dictionary<string, decimal> categoryAveragePrices = meals
			.GroupBy(meal => meal.Category, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(
				group => group.Key,
				group => decimal.Round(group.Average(meal => meal.Price), 2),
				StringComparer.OrdinalIgnoreCase);

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

		for (int i = 0; i < dominantCategories.Length; i++)
		{
			string category = dominantCategories[i];
			string protein = ProteinTerms[i % ProteinTerms.Length];
			string style = StyleTerms[(i + 2) % StyleTerms.Length];
			string flavor = FlavorTerms[(i + 4) % FlavorTerms.Length];
			string keyword = topKeywords.Length == 0 ? "seasonal" : topKeywords[(i * 2) % topKeywords.Length];
			string candidateName = $"{flavor} {protein} {style}";

			if (existingNames.Contains(candidateName) || generatedNames.Contains(candidateName))
			{
				candidateName = $"{flavor} {protein} {style} Special";
			}

			generatedNames.Add(candidateName);

			decimal baseline = categoryAveragePrices.TryGetValue(category, out decimal avgPrice)
				? avgPrice
				: meals.Average(meal => meal.Price);
			float predictedPrice = float.Round((float)(baseline + ((i - 1) * 0.75m)), 2);
			if (predictedPrice < 5f)
			{
				predictedPrice = 5f;
			}

			float confidence = float.Round(Math.Max(0.45f, 0.78f - (i * 0.1f)), 2);
			string rationale = $"This category appears frequently, and keywords like '{keyword}' recur across announced meals, suggesting a similar upcoming item.";
			string[] predictionKeywords = [
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

	private static IReadOnlyList<string> ExtractKeywords(string name, string description)
	{
		HashSet<string> stopWords = new(StringComparer.OrdinalIgnoreCase)
		{
			"and", "the", "with", "for", "your", "our", "fresh", "includes", "include", "served", "service",
			"buffet", "box", "boxed", "person", "meal", "meals", "assorted", "choice", "selection", "style",
			"house", "made", "day", "available", "option", "options", "add", "ice", "water", "tea", "coffee"
		};

		return [.. WordRegex()
			.Matches($"{name} {description}")
			.Select(match => match.Value.Trim().ToLowerInvariant())
			.Where(word => word.Length > 3 && !stopWords.Contains(word))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(8)];
	}

	private static bool TryParsePrice(string priceText, out decimal price)
	{
		return decimal.TryParse(
			priceText.Replace("$", string.Empty, StringComparison.Ordinal),
			NumberStyles.Number,
			CultureInfo.InvariantCulture,
			out price);
	}

	private static string? ExtractFirstAbsoluteUrl(string html, string hostHint)
	{
		foreach (Match match in HrefRegex().Matches(html))
		{
			string href = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim();
			if (href.Contains(hostHint, StringComparison.OrdinalIgnoreCase))
			{
				return href;
			}
		}

		return null;
	}

	private static string MakeAbsoluteUrl(string baseUrl, string href)
	{
		return new Uri(new Uri(baseUrl), WebUtility.HtmlDecode(href)).ToString();
	}

	private static string StripHtml(string value)
	{
		string withoutBreaks = value.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("<br />", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("</p>", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("<p>", " ", StringComparison.OrdinalIgnoreCase);

		return StripHTML().Replace(withoutBreaks, " ");
	}

	private static string NormalizeText(string value)
	{
		return NormalizeText().Replace(WebUtility.HtmlDecode(value), " ").Trim();
	}

	[GeneratedRegex("href\\s*=\\s*['\"](?<href>[^'\"]+)['\"]", RegexOptions.IgnoreCase)]
	private static partial Regex HrefRegex();

	[GeneratedRegex("<a class='category-tile' href='(?<href>[^']+)'.*?<div class='tile-title'>(?<name>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex CategoryRegex();

	[GeneratedRegex("<div class='product-slot'>\\s*<a class='product-tile' href=\"(?<href>[^\"]+)\">.*?<div class='tile-title'>(?<name>.*?)</div>.*?<div class='tile-description'>(?<description>.*?)</div>.*?<div class='cost'>(?<price>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex ProductRegex();

	[GeneratedRegex("[A-Za-z][A-Za-z'`-]+", RegexOptions.CultureInvariant)]
	private static partial Regex WordRegex();

	private sealed record CategoryLink(string Name, string Url);

	[GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
	private static partial Regex StripHTML();

	[GeneratedRegex("\\s+")]
	private static partial Regex NormalizeText();
}