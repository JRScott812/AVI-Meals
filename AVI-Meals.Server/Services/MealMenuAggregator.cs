using AVI_Meals.Server.Models;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Deduplicates daily menu rows and builds combined occurrence stats.
/// </summary>
public static class MealMenuAggregator
{
	private static readonly MealType[] MealTypeOrder =
	[
		MealType.Breakfast,
		MealType.Brunch,
		MealType.Lunch,
		MealType.Dinner
	];

	/// <summary>
	/// Within each day, combines identical meal name + station + meal type rows (case-insensitive)
	/// and unions their tags.
	/// </summary>
	public static IReadOnlyList<DailyMenu> DeduplicateDailyMenus(IReadOnlyList<DailyMenu> dailyMenus)
	{
		return [.. dailyMenus
			.Select(day => new DailyMenu(
				day.Date,
				[.. day.Items
					.Where(item => !string.IsNullOrWhiteSpace(item.MealName))
					.GroupBy(
						item => (Name: item.MealName.Trim(), item.Station, item.MealType),
						new DailyItemKeyComparer())
					.Select(group =>
					{
						DailyMenuItem first = group.First();
						string[] tags = [.. group
							.SelectMany(item => item.Tags)
							.Where(tag => !string.IsNullOrWhiteSpace(tag))
							.Distinct(StringComparer.OrdinalIgnoreCase)
							.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)];
						return new DailyMenuItem(
							first.MealName.Trim(),
							first.Station,
							first.MealType,
							string.IsNullOrWhiteSpace(first.Category)
								? group.Select(item => item.Category).FirstOrDefault(category => !string.IsNullOrWhiteSpace(category)) ?? string.Empty
								: first.Category.Trim(),
							tags);
					})
					.OrderBy(item => item.Station)
					.ThenBy(item => item.MealType)
					.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)]))
			.OrderBy(day => day.Date)];
	}

	/// <summary>
	/// Combines duplicate dish names across history into a single row with an occurrence count
	/// (total menu listings) and the meal periods where it appears.
	/// </summary>
	public static IReadOnlyList<MealOccurrence> BuildOccurrences(
		IReadOnlyList<DailyMenu> dailyMenus,
		IReadOnlyList<MealItem>? catalogFallback = null)
	{
		IReadOnlyList<DailyMenu> normalized = DeduplicateDailyMenus(dailyMenus);
		List<(string Name, MealType MealType)> listings = [.. normalized
			.SelectMany(day => day.Items.Select(item => (Name: item.MealName.Trim(), item.MealType)))
			.Where(entry => !string.IsNullOrWhiteSpace(entry.Name))];

		if (listings.Count > 0)
		{
			return [.. listings
				.GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.Select(group => new MealOccurrence(
					group.First().Name,
					group.Count(),
					[.. group
						.Select(entry => entry.MealType)
						.Where(mealType => mealType != MealType.Unknown)
						.Distinct()
						.OrderBy(mealType =>
						{
							int index = Array.IndexOf(MealTypeOrder, mealType);
							return index >= 0 ? index : int.MaxValue;
						})
						.ThenBy(mealType => mealType)]))
				.OrderByDescending(item => item.OccurrenceCount)
				.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)];
		}

		if (catalogFallback is null || catalogFallback.Count == 0)
		{
			return [];
		}

		return [.. catalogFallback
			.Select(meal => meal.Name.Trim())
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
			.Select(group => new MealOccurrence(group.First(), group.Count(), []))
			.OrderByDescending(item => item.OccurrenceCount)
			.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)];
	}

	private sealed class DailyItemKeyComparer
		: IEqualityComparer<(string Name, DiningStation Station, MealType MealType)>
	{
		public bool Equals(
			(string Name, DiningStation Station, MealType MealType) x,
			(string Name, DiningStation Station, MealType MealType) y) =>
			x.Station == y.Station
			&& x.MealType == y.MealType
			&& string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

		public int GetHashCode((string Name, DiningStation Station, MealType MealType) obj) =>
			HashCode.Combine(
				StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
				obj.Station,
				obj.MealType);
	}
}
