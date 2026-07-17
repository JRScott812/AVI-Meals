using System.Text.Json;

using AVI_Meals.Server.Data;
using AVI_Meals.Server.Models;

using Microsoft.EntityFrameworkCore;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Persists and reads meal history when a database connection is configured.
/// </summary>
public sealed class MealHistoryStore(IServiceScopeFactory scopeFactory, ILogger<MealHistoryStore> logger)
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly SemaphoreSlim _backfillLock = new(1, 1);
	private bool _backfillAttempted;

	public async Task UpsertDailyMenusAsync(
		IReadOnlyList<DailyMenu> dailyMenus,
		int? locationId,
		CancellationToken cancellationToken)
	{
		if (dailyMenus.Count == 0)
		{
			return;
		}

		// History is Hodson residential dining only (AVI Dish location 183).
		if (locationId != DishMenuClient.HodsonLocationId)
		{
			logger.LogInformation(
				"Skipping history upsert for non-Hodson location {LocationId}",
				locationId);
			return;
		}

		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		MealsDbContext? db = scope.ServiceProvider.GetService<MealsDbContext>();
		if (db is null)
		{
			return;
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (DailyMenu day in dailyMenus)
		{
			cancellationToken.ThrowIfCancellationRequested();

			MenuDayEntity? existingDay = await db.MenuDays
				.Include(menuDay => menuDay.Items)
				.FirstOrDefaultAsync(menuDay => menuDay.Date == day.Date, cancellationToken)
				.ConfigureAwait(false);

			if (existingDay is null)
			{
				existingDay = new MenuDayEntity
				{
					Date = day.Date,
					LocationId = locationId,
					UpdatedAtUtc = now
				};
				db.MenuDays.Add(existingDay);
			}
			else
			{
				existingDay.LocationId = locationId;
				existingDay.UpdatedAtUtc = now;
				db.MenuItems.RemoveRange(existingDay.Items);
				existingDay.Items.Clear();
			}

			foreach (DailyMenuItem item in DeduplicateDayItems(day.Items))
			{
				existingDay.Items.Add(new MenuItemEntity
				{
					MealName = item.MealName,
					Station = item.Station.ToString(),
					MealType = item.MealType.ToString(),
					Category = item.Category,
					TagsJson = JsonSerializer.Serialize(item.Tags, JsonOptions)
				});
			}
		}

		_ = await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task RecordScrapeRunAsync(
		string portalUrl,
		string diningUrl,
		string menuUrl,
		int dailyMenuCount,
		int catalogMealCount,
		string status,
		CancellationToken cancellationToken)
	{
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		MealsDbContext? db = scope.ServiceProvider.GetService<MealsDbContext>();
		if (db is null)
		{
			return;
		}

		db.ScrapeRuns.Add(new ScrapeRunEntity
		{
			RetrievedAtUtc = DateTimeOffset.UtcNow,
			PortalUrl = portalUrl,
			DiningUrl = diningUrl,
			MenuUrl = menuUrl,
			Status = status,
			DailyMenuCount = dailyMenuCount,
			CatalogMealCount = catalogMealCount
		});

		_ = await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<DailyMenu>> GetStoredDailyMenusAsync(CancellationToken cancellationToken)
	{
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		MealsDbContext? db = scope.ServiceProvider.GetService<MealsDbContext>();
		if (db is null)
		{
			return [];
		}

		List<MenuDayEntity> days = await db.MenuDays
			.AsNoTracking()
			.Include(day => day.Items)
			.OrderBy(day => day.Date)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return [.. days.Select(day => new DailyMenu(
			day.Date,
			[.. day.Items
				.OrderBy(item => item.Station, StringComparer.OrdinalIgnoreCase)
				.ThenBy(item => item.MealType, StringComparer.OrdinalIgnoreCase)
				.ThenBy(item => item.MealName, StringComparer.OrdinalIgnoreCase)
				.Select(item => new DailyMenuItem(
					item.MealName,
					ParseStoredStation(item.Station),
					ParseStoredMealType(item.MealType, item.Station),
					item.Category,
					DeserializeStringList(item.TagsJson)))]))];
	}

	public async Task EnsureBackfillAsync(
		Func<DateOnly, CancellationToken, Task<IReadOnlyList<DailyMenu>>> fetchWeekAsync,
		int? locationId,
		CancellationToken cancellationToken,
		bool force = false,
		int maxWeeks = 104)
	{
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		MealsDbContext? db = scope.ServiceProvider.GetService<MealsDbContext>();
		if (db is null)
		{
			return;
		}

		await _backfillLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!force && _backfillAttempted)
			{
				return;
			}

			_backfillAttempted = true;

			int existingDays = await db.MenuDays.CountAsync(cancellationToken).ConfigureAwait(false);
			if (!force && existingDays > 0)
			{
				return;
			}

			if (force)
			{
				await db.MenuItems.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
				await db.MenuDays.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
				logger.LogInformation("Cleared existing Hodson meal history before forced backfill");
			}

			if (locationId != DishMenuClient.HodsonLocationId)
			{
				logger.LogInformation(
					"Skipping Dish backfill for non-Hodson location {LocationId}",
					locationId);
				return;
			}

			const int emptyWeeksToStop = 4;
			int consecutiveEmptyWeeks = 0;
			int weeksWithData = 0;
			logger.LogInformation(
				"Starting Dish menu history backfill (force={Force}, maxWeeks={MaxWeeks})",
				force,
				maxWeeks);

			DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
			for (int weekOffset = 0; weekOffset < maxWeeks; weekOffset++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				DateOnly anchor = today.AddDays(-7 * weekOffset);
				try
				{
					IReadOnlyList<DailyMenu> weekMenus = await fetchWeekAsync(anchor, cancellationToken)
						.ConfigureAwait(false);
					if (weekMenus.Count == 0)
					{
						consecutiveEmptyWeeks++;
						logger.LogInformation("No Dish data for week anchor {Anchor:yyyy-MM-dd}", anchor);
						if (consecutiveEmptyWeeks >= emptyWeeksToStop)
						{
							logger.LogInformation(
								"Stopping backfill after {Empty} consecutive empty weeks ({WeeksWithData} weeks with data)",
								consecutiveEmptyWeeks,
								weeksWithData);
							break;
						}
					}
					else
					{
						consecutiveEmptyWeeks = 0;
						weeksWithData++;
						await UpsertDailyMenusAsync(weekMenus, locationId, cancellationToken).ConfigureAwait(false);
						logger.LogInformation(
							"Backfilled week anchor {Anchor:yyyy-MM-dd} ({Count} days)",
							anchor,
							weekMenus.Count);
					}
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					consecutiveEmptyWeeks++;
					logger.LogWarning(exception, "Backfill failed for week anchor {Anchor:yyyy-MM-dd}", anchor);
					if (consecutiveEmptyWeeks >= emptyWeeksToStop)
					{
						break;
					}
				}

				await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
			}

			logger.LogInformation("Dish backfill finished with {WeeksWithData} weeks containing data", weeksWithData);
		}
		finally
		{
			_ = _backfillLock.Release();
		}
	}

	public static IReadOnlyList<DailyMenu> MergeDailyMenus(
		IReadOnlyList<DailyMenu> live,
		IReadOnlyList<DailyMenu> stored)
	{
		Dictionary<DateOnly, DailyMenu> byDate = new();
		foreach (DailyMenu day in stored.Concat(live))
		{
			byDate[day.Date] = day;
		}

		return MealMenuAggregator.DeduplicateDailyMenus([.. byDate.Values.OrderBy(day => day.Date)]);
	}

	private static IReadOnlyList<DailyMenuItem> DeduplicateDayItems(IReadOnlyList<DailyMenuItem> items) =>
		MealMenuAggregator.DeduplicateDailyMenus([new DailyMenu(DateOnly.MinValue, items)])[0].Items;

	private static DiningStation ParseStoredStation(string value) =>
		Enum.TryParse(value, ignoreCase: true, out DiningStation station)
			? station
			: MealTaxonomy.ParseStation(value);

	private static MealType ParseStoredMealType(string mealTypeValue, string stationValue)
	{
		if (Enum.TryParse(mealTypeValue, ignoreCase: true, out MealType mealType)
			&& mealType != MealType.Unknown)
		{
			return mealType;
		}

		return MealTaxonomy.ParseMealType(stationValue);
	}

	private static IReadOnlyList<string> DeserializeStringList(string json)
	{
		try
		{
			return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}
}
