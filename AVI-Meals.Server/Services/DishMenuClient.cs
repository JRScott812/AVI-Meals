using System.Globalization;
using System.Text.Json;

using AVI_Meals.Server.Models;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Loads daily menus from the AVI Dish JSON APIs.
/// </summary>
internal sealed class DishMenuClient(DiningHttpFetcher http)
{
	private const string DishClientUrlFormat = "https://dish.avifoodsystems.com/api/client?clientName={0}";
	private const string DishMenuWeekUrlFormat =
		"https://dish.avifoodsystems.com/api/menu-items/week?date={0}&locationId={1}&mealId={2}";
	private const string DefaultClientName = "taylor";
	private const int PreferredTaylorLocationId = 183;

	public async Task<IReadOnlyList<DailyMenu>> BuildDailyMenusAsync(string dishUrl, CancellationToken cancellationToken)
	{
		DishRouteContext routeContext = ParseDishRouteContext(dishUrl);
		DishClientContext? clientContext = await ResolveClientContextAsync(routeContext, cancellationToken)
			.ConfigureAwait(false);
		if (clientContext is null)
		{
			return [];
		}

		DateOnly anchorDate = routeContext.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
		IReadOnlyList<int> mealIds = ResolveMealIds(routeContext.MealId, clientContext.MealIds);
		return await BuildDailyMenusForDateAsync(anchorDate, clientContext.LocationId, mealIds, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Loads one Dish menu week for an explicit anchor date and location.
	/// </summary>
	public async Task<IReadOnlyList<DailyMenu>> BuildDailyMenusForDateAsync(
		DateOnly anchorDate,
		int locationId,
		IReadOnlyList<int>? mealIds,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<int> ids = mealIds is { Count: > 0 } ? mealIds : [];
		if (ids.Count == 0)
		{
			return [];
		}

		string dateParameter = anchorDate.ToString("M/d/yyyy", CultureInfo.InvariantCulture);

		List<DishMenuItemRaw> weeklyItems = [];
		foreach (int mealId in ids)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string endpoint = string.Format(CultureInfo.InvariantCulture, DishMenuWeekUrlFormat, dateParameter, locationId, mealId);
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
			.GroupBy(item => (item.Date, MealName: item.MealName.Trim(), Station: item.Station.Trim()),
				new DishItemKeyComparer())
			.Select(group => group.First())
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

	public async Task<int?> ResolveLocationIdAsync(string dishUrl, CancellationToken cancellationToken)
	{
		DishRouteContext routeContext = ParseDishRouteContext(dishUrl);
		DishClientContext? clientContext = await ResolveClientContextAsync(routeContext, cancellationToken)
			.ConfigureAwait(false);
		return clientContext?.LocationId;
	}

	/// <summary>
	/// Resolves Taylor Dish location and meal-period IDs used by the week menu API.
	/// </summary>
	public async Task<(int LocationId, IReadOnlyList<int> MealIds)?> ResolveLocationAndMealIdsAsync(
		string dishUrl,
		CancellationToken cancellationToken)
	{
		DishRouteContext routeContext = ParseDishRouteContext(dishUrl);
		DishClientContext? clientContext = await ResolveClientContextAsync(routeContext, cancellationToken)
			.ConfigureAwait(false);
		return clientContext is null
			? null
			: (clientContext.LocationId, clientContext.MealIds);
	}

	private async Task<DishClientContext?> ResolveClientContextAsync(
		DishRouteContext routeContext,
		CancellationToken cancellationToken)
	{
		string clientName = string.IsNullOrWhiteSpace(routeContext.ClientName)
			? DefaultClientName
			: routeContext.ClientName;
		string endpoint = string.Format(CultureInfo.InvariantCulture, DishClientUrlFormat, Uri.EscapeDataString(clientName));
		string? payload = await http.TryGetJsonAsync(endpoint, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(payload))
		{
			return null;
		}

		using JsonDocument document = JsonDocument.Parse(payload);
		if (document.RootElement.ValueKind != JsonValueKind.Object
			|| !document.RootElement.TryGetProperty("locations", out JsonElement locationsElement)
			|| locationsElement.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		List<DishLocationRaw> locations = [];
		foreach (JsonElement location in locationsElement.EnumerateArray())
		{
			if (!TryParseLocation(location, out DishLocationRaw? parsed) || parsed is null)
			{
				continue;
			}

			locations.Add(parsed);
		}

		if (locations.Count == 0)
		{
			return null;
		}

		DishLocationRaw? selected = null;
		if (routeContext.LocationId is int routeLocationId)
		{
			selected = locations.FirstOrDefault(location => location.Id == routeLocationId);
		}

		selected ??= locations.FirstOrDefault(location => location.Id == PreferredTaylorLocationId);
		selected ??= locations.FirstOrDefault(location =>
			location.Name.Contains("Hodson", StringComparison.OrdinalIgnoreCase));
		selected ??= locations.FirstOrDefault(location => location.IsEnabled);
		selected ??= locations[0];

		if (selected.MealIds.Count == 0)
		{
			return null;
		}

		return new DishClientContext(selected.Id, selected.MealIds);
	}

	private static IReadOnlyList<int> ResolveMealIds(int? routeMealId, IReadOnlyList<int> locationMealIds)
	{
		if (routeMealId is int mealId && locationMealIds.Contains(mealId))
		{
			return [mealId];
		}

		return locationMealIds;
	}

	private static DishRouteContext ParseDishRouteContext(string dishUrl)
	{
		if (!Uri.TryCreate(dishUrl, UriKind.Absolute, out Uri? uri))
		{
			return DishRouteContext.Empty;
		}

		string[] segments = uri.AbsolutePath
			.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (segments.Length == 0)
		{
			return DishRouteContext.Empty;
		}

		string clientName = segments[0];
		int? locationId = segments.Length > 1
			&& int.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLocationId)
			? parsedLocationId
			: null;
		int? mealId = segments.Length > 2
			&& int.TryParse(segments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMealId)
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

		return new DishRouteContext(clientName, locationId, mealId, date);
	}

	private static bool TryParseLocation(JsonElement location, out DishLocationRaw? parsed)
	{
		parsed = null;
		if (!location.TryGetProperty("id", out JsonElement idElement)
			|| idElement.ValueKind != JsonValueKind.Number
			|| !idElement.TryGetInt32(out int locationId))
		{
			return false;
		}

		string name = location.TryGetProperty("name", out JsonElement nameElement)
			&& nameElement.ValueKind == JsonValueKind.String
			? nameElement.GetString() ?? string.Empty
			: string.Empty;
		bool isEnabled = !location.TryGetProperty("isEnabled", out JsonElement enabledElement)
			|| enabledElement.ValueKind != JsonValueKind.False;

		List<int> mealIds = [];
		if (location.TryGetProperty("meals", out JsonElement mealsElement)
			&& mealsElement.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement meal in mealsElement.EnumerateArray())
			{
				if (meal.TryGetProperty("id", out JsonElement mealIdElement)
					&& mealIdElement.ValueKind == JsonValueKind.Number
					&& mealIdElement.TryGetInt32(out int mealId))
				{
					mealIds.Add(mealId);
				}
			}
		}

		parsed = new DishLocationRaw(locationId, name, isEnabled, mealIds);
		return true;
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
			|| !TryParseDishDate(dateText, out DateOnly date))
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

	private static bool TryParseDishDate(string dateText, out DateOnly date)
	{
		string[] formats =
		[
			"M/d/yyyy HH:mm:ss",
			"M/d/yyyy H:mm:ss",
			"MM/dd/yyyy HH:mm:ss",
			"M/d/yyyy",
			"MM/dd/yyyy",
			"yyyy-MM-dd",
			"yyyy-MM-ddTHH:mm:ss",
			"yyyy-MM-ddTHH:mm:ss.fff"
		];

		if (DateOnly.TryParseExact(dateText, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
		{
			return true;
		}

		if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime dateTime))
		{
			date = DateOnly.FromDateTime(dateTime);
			return true;
		}

		date = default;
		return false;
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

	private sealed record DishRouteContext(string ClientName, int? LocationId, int? MealId, DateOnly? Date)
	{
		public static DishRouteContext Empty { get; } = new(DefaultClientName, null, null, null);
	}

	private sealed record DishClientContext(int LocationId, IReadOnlyList<int> MealIds);

	private sealed record DishLocationRaw(int Id, string Name, bool IsEnabled, IReadOnlyList<int> MealIds);

	private sealed record DishMenuItemRaw(
		DateOnly Date,
		string MealName,
		string Station,
		string Category,
		decimal? Price,
		IReadOnlyList<string> Tags);

	private sealed class DishItemKeyComparer : IEqualityComparer<(DateOnly Date, string MealName, string Station)>
	{
		public bool Equals((DateOnly Date, string MealName, string Station) x, (DateOnly Date, string MealName, string Station) y) =>
			x.Date == y.Date
			&& string.Equals(x.MealName, y.MealName, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(x.Station, y.Station, StringComparison.OrdinalIgnoreCase);

		public int GetHashCode((DateOnly Date, string MealName, string Station) obj) =>
			HashCode.Combine(
				obj.Date,
				StringComparer.OrdinalIgnoreCase.GetHashCode(obj.MealName),
				StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Station));
	}
}
