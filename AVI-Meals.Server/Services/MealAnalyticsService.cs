using AVI_Meals.Server.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AVI_Meals.Server.Services;

public sealed partial class MealAnalyticsService(HttpClient httpClient, IMemoryCache cache)
{
	private const string CacheKey = "meal-analytics";
	private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
	private const string PortalUrl = "https://connecttaylor.atriumcampus.com/index.php";
	private const string DishSiteUrl = "https://dish.avifoodsystems.com/taylor";
	private const string DishLocationsUrl = "https://dish.avifoodsystems.com/api/locations?client=taylor";
	private const string DishMenuWeekUrlFormat = "https://dish.avifoodsystems.com/api/menu-items/week?date={0}&locationId={1}&mealId={2}";
	private static readonly int[] DishMealIds = [1, 2, 3, 4, 5, 6];

	private static readonly string[] ProteinTerms = ["Chicken", "Turkey", "Pork", "Beef", "Tofu", "Veggie", "Falafel", "Egg"];
	private static readonly string[] StyleTerms = ["Bowl", "Wrap", "Skillet", "Pasta", "Salad", "Flatbread", "Tacos", "Sandwich"];
	private static readonly string[] FlavorTerms = ["Garden", "Smoky", "Harvest", "Herb", "Citrus", "Maple", "Southwest", "Mediterranean"];

	/// <summary>
	/// Gets the latest meal analytics snapshot from the public dining sources.
	/// </summary>
	public async Task<MealAnalyticsResponse> GetAnalyticsAsync(CancellationToken cancellationToken)
	{
		MealAnalyticsResponse? result = await cache.GetOrCreateAsync(CacheKey, async entry =>
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
		string dishUrl = ExtractFirstAbsoluteUrl(diningHtml, "dish.avifoodsystems.com") ?? DishSiteUrl;

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

		IReadOnlyList<DailyMenu> dailyMenus = await BuildDishDailyMenusAsync(dishUrl, cancellationToken).ConfigureAwait(false);

		MealItem[] orderedMeals = [.. meals
			.OrderBy(meal => meal.Category, StringComparer.OrdinalIgnoreCase)
			.ThenBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)];
		IReadOnlyList<MealOccurrence> mealOccurrences = BuildMealOccurrences(orderedMeals, dailyMenus);

		return new MealAnalyticsResponse(
			PortalUrl,
			diningUrl,
			menuUrl,
			DateTimeOffset.UtcNow,
			BuildSummary(orderedMeals),
			orderedMeals,
			BuildHeatmaps(orderedMeals),
			BuildPredictions(orderedMeals),
			BuildUnannouncedMealPredictions(orderedMeals),
			dailyMenus,
			mealOccurrences);
	}

	/// <summary>
	/// Builds a distinct meal-name collection with occurrence counts across menu sources.
	/// </summary>
	private static IReadOnlyList<MealOccurrence> BuildMealOccurrences(
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

		MealOccurrence[] occurrences = [.. primaryNames
			.Concat(dailyNames)
			.GroupBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(group => new MealOccurrence(group.First(), group.Count()))
			.OrderByDescending(item => item.OccurrenceCount)
			.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)];

		return occurrences;
	}

	/// <summary>
	/// Builds daily menus from AVI Dish, honoring location/meal/date route segments when present.
	/// </summary>
	private async Task<IReadOnlyList<DailyMenu>> BuildDishDailyMenusAsync(string dishUrl, CancellationToken cancellationToken)
	{
		DishRouteContext routeContext = ParseDishRouteContext(dishUrl);
		int? locationId = routeContext.LocationId ?? await ResolveDishLocationIdAsync(cancellationToken).ConfigureAwait(false);
		if (!locationId.HasValue)
		{
			return [];
		}

		DateOnly anchorDate = routeContext.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
		string dateParameter = anchorDate.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
		IReadOnlyList<int> mealIds = routeContext.MealId.HasValue ? [routeContext.MealId.Value] : DishMealIds;

		List<DishMenuItemRaw> weeklyItems = [];
		foreach (int mealId in mealIds)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string endpoint = string.Format(CultureInfo.InvariantCulture, DishMenuWeekUrlFormat, dateParameter, locationId.Value, mealId);
			string? payload = await TryGetJsonAsync(endpoint, cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(payload))
			{
				continue;
			}

			weeklyItems.AddRange(ParseDishMenuItems(payload));
		}

		if (weeklyItems.Count == 0)
		{
			return [];
		}

		DailyMenu[] dailyMenus = [.. weeklyItems
			.GroupBy(item => item.Date)
			.OrderBy(group => group.Key)
			.Select(group => new DailyMenu(
				group.Key,
				[.. group
					.OrderBy(item => item.Station, StringComparer.OrdinalIgnoreCase)
					.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)
					.Select(item => new DailyMenuItem(item.MealName, item.Station, item.Category, item.Price, item.Tags))
				]))];

		return dailyMenus;
	}

	private static DishRouteContext ParseDishRouteContext(string dishUrl)
	{
		if (!Uri.TryCreate(dishUrl, UriKind.Absolute, out Uri? uri))
		{
			return DishRouteContext.Empty;
		}

		string[] segments = uri.AbsolutePath
			.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (segments.Length < 2)
		{
			return DishRouteContext.Empty;
		}

		int? locationId = int.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLocationId)
			? parsedLocationId
			: null;
		int? mealId = segments.Length > 2 && int.TryParse(segments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMealId)
			? parsedMealId
			: null;
		DateOnly? date = null;
		if (segments.Length > 3)
		{
			string rawDate = segments[3];
			string[] acceptedFormats = ["M/d/yyyy", "M-d-yyyy", "yyyy-MM-dd", "MM-dd-yyyy"];
			if (DateOnly.TryParseExact(rawDate, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateOnly parsedDate)
				|| DateOnly.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate))
			{
				date = parsedDate;
			}
		}

		return new DishRouteContext(locationId, mealId, date);
	}

	private async Task<int?> ResolveDishLocationIdAsync(CancellationToken cancellationToken)
	{
		string? payload = await TryGetJsonAsync(DishLocationsUrl, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(payload))
		{
			return null;
		}

		using JsonDocument document = JsonDocument.Parse(payload);
		if (document.RootElement.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		foreach (JsonElement location in document.RootElement.EnumerateArray())
		{
			if (!location.TryGetProperty("name", out JsonElement nameElement)
				|| nameElement.ValueKind != JsonValueKind.String)
			{
				continue;
			}

			string? name = nameElement.GetString();
			if (string.IsNullOrWhiteSpace(name)
				|| !name.Contains("Taylor", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (!location.TryGetProperty("id", out JsonElement idElement)
				|| idElement.ValueKind != JsonValueKind.Number)
			{
				continue;
			}

			if (idElement.TryGetInt32(out int locationId))
			{
				return locationId;
			}
		}

		return null;
	}

	private static IReadOnlyList<DishMenuItemRaw> ParseDishMenuItems(string payload)
	{
		using JsonDocument document = JsonDocument.Parse(payload);
		if (document.RootElement.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		List<DishMenuItemRaw> items = [];
		foreach (JsonElement element in document.RootElement.EnumerateArray())
		{
			if (!TryParseDishMenuItem(element, out DishMenuItemRaw? item) || item is null)
			{
				continue;
			}

			items.Add(item);
		}

		return items;
	}

	private static bool TryParseDishMenuItem(JsonElement element, out DishMenuItemRaw? item)
	{
		item = null;

		if (!TryGetPropertyString(element, "name", out string mealName)
			|| !TryGetPropertyString(element, "date", out string dateText)
			|| !DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateOnly date))
		{
			return false;
		}

		string station = TryGetPropertyString(element, "stationName", out string stationName)
			? stationName
			: "General";
		string category = TryGetPropertyString(element, "categoryName", out string categoryName)
			? categoryName
			: "Uncategorized";
		decimal? price = TryGetPropertyDecimal(element, "price", out decimal parsedPrice)
			? parsedPrice
			: null;

		List<string> tags = [];
		if (element.TryGetProperty("preferences", out JsonElement preferencesElement)
			&& preferencesElement.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement preference in preferencesElement.EnumerateArray())
			{
				if (TryGetPropertyString(preference, "name", out string preferenceName))
				{
					tags.Add(preferenceName);
				}
			}
		}

		if (element.TryGetProperty("allergens", out JsonElement allergensElement)
			&& allergensElement.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement allergen in allergensElement.EnumerateArray())
			{
				if (TryGetPropertyString(allergen, "name", out string allergenName))
				{
					tags.Add($"Contains {allergenName}");
				}
			}
		}

		item = new DishMenuItemRaw(
			date,
			mealName,
			station,
			category,
			price,
			[.. tags.Distinct(StringComparer.OrdinalIgnoreCase)]);
		return true;
	}

	private static bool TryGetPropertyString(JsonElement element, string propertyName, out string value)
	{
		value = string.Empty;
		if (!element.TryGetProperty(propertyName, out JsonElement property)
			|| property.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		string? parsed = property.GetString();
		if (string.IsNullOrWhiteSpace(parsed))
		{
			return false;
		}

		value = parsed;
		return true;
	}

	private static bool TryGetPropertyDecimal(JsonElement element, string propertyName, out decimal value)
	{
		value = 0m;
		if (!element.TryGetProperty(propertyName, out JsonElement property))
		{
			return false;
		}

		if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out decimal directValue))
		{
			value = directValue;
			return true;
		}

		if (property.ValueKind == JsonValueKind.String
			&& decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal textValue))
		{
			value = textValue;
			return true;
		}

		return false;
	}

	private async Task<string?> TryGetJsonAsync(string url, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (payload.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
			|| payload.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return payload;
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
			string name = NormalizeTextValue(match.Groups["name"].Value);
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
			string name = NormalizeTextValue(match.Groups["name"].Value);
			string description = NormalizeTextValue(StripHtml(match.Groups["description"].Value));
			string priceText = NormalizeTextValue(match.Groups["price"].Value);
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

	private static MealSummary BuildSummary(IReadOnlyList<MealItem> meals)
	{
		string[] categories = [.. meals
			.Select(meal => meal.Category)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

		return new MealSummary(
			meals.Count,
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

	private static IReadOnlyList<Prediction> BuildPredictions(IReadOnlyList<MealItem> meals)
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

		decimal confidence = decimal.Round(dominantCategory.Count() / (decimal)meals.Count, 2);
		decimal priceConfidence = decimal.Round(dominantBand.Count() / (decimal)meals.Count, 2);

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
				topKeywords.Length == 0 ? 0m : 0.65m)
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

		for (int index = 0; index < dominantCategories.Length; index++)
		{
			string category = dominantCategories[index];
			string protein = ProteinTerms[index % ProteinTerms.Length];
			string style = StyleTerms[(index + 2) % StyleTerms.Length];
			string flavor = FlavorTerms[(index + 4) % FlavorTerms.Length];
			string keyword = topKeywords.Length == 0 ? "seasonal" : topKeywords[(index * 2) % topKeywords.Length];
			string candidateName = $"{flavor} {protein} {style}";

			if (existingNames.Contains(candidateName) || generatedNames.Contains(candidateName))
			{
				candidateName = $"{flavor} {protein} {style} Special";
			}

			generatedNames.Add(candidateName);

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

		return HtmlTagRegex().Replace(withoutBreaks, " ");
	}

	private static string NormalizeTextValue(string value)
	{
		return WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();
	}

	[GeneratedRegex("href\\s*=\\s*['\"](?<href>[^'\"]+)['\"]", RegexOptions.IgnoreCase)]
	private static partial Regex HrefRegex();

	[GeneratedRegex("<a class='category-tile' href='(?<href>[^']+)'.*?<div class='tile-title'>(?<name>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex CategoryRegex();

	[GeneratedRegex("<div class='product-slot'>\\s*<a class='product-tile' href=\"(?<href>[^\"]+)\">.*?<div class='tile-title'>(?<name>.*?)</div>.*?<div class='tile-description'>(?<description>.*?)</div>.*?<div class='cost'>(?<price>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex ProductRegex();

	[GeneratedRegex("[A-Za-z][A-Za-z'`-]+", RegexOptions.CultureInvariant)]
	private static partial Regex WordRegex();

	[GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
	private static partial Regex HtmlTagRegex();

	[GeneratedRegex("\\s+")]
	private static partial Regex WhitespaceRegex();

	private sealed record DishRouteContext(int? LocationId, int? MealId, DateOnly? Date)
	{
		public static DishRouteContext Empty { get; } = new(null, null, null);
	}

	private sealed record DishMenuItemRaw(
		DateOnly Date,
		string MealName,
		string Station,
		string Category,
		decimal? Price,
		IReadOnlyList<string> Tags);

	private sealed record CategoryLink(string Name, string Url);
}