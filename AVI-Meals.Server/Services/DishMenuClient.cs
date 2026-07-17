using System.Globalization;
using System.Text.Json;

using AVI_Meals.Server.Models;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Loads daily menus from the AVI Dish JSON APIs.
/// </summary>
internal sealed class DishMenuClient(DiningHttpFetcher http)
{
	private const string DishLocationsUrl = "https://dish.avifoodsystems.com/api/locations?client=taylor";
	private const string DishMenuWeekUrlFormat = "https://dish.avifoodsystems.com/api/menu-items/week?date={0}&locationId={1}&mealId={2}";
	private static readonly int[] DishMealIds = [1, 2, 3, 4, 5, 6];

	public async Task<IReadOnlyList<DailyMenu>> BuildDailyMenusAsync(string dishUrl, CancellationToken cancellationToken)
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
			string? payload = await http.TryGetJsonAsync(endpoint, cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(payload))
			{
				continue;
			}

			weeklyItems.AddRange(ParseDishMenuItems(payload));
		}

		return weeklyItems.Count == 0
			? []
			: [.. weeklyItems
			.GroupBy(item => item.Date)
			.OrderBy(group => group.Key)
			.Select(group => new DailyMenu(
				group.Key,
				[.. group
					.OrderBy(item => item.Station, StringComparer.OrdinalIgnoreCase)
					.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)
					.Select(item => new DailyMenuItem(item.MealName, item.Station, item.Category, item.Price, item.Tags))
				]))];
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
		string? payload = await http.TryGetJsonAsync(DishLocationsUrl, cancellationToken).ConfigureAwait(false);
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
}
