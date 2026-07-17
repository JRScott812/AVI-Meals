using AVI_Meals.Server.Models;

using Microsoft.Extensions.Caching.Memory;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Orchestrates dining-source fetches, optional history persistence, and analytics.
/// </summary>
public sealed class MealAnalyticsService(
	HttpClient httpClient,
	IMemoryCache cache,
	MealHistoryStore historyStore,
	ILogger<MealAnalyticsService> logger)
{
	private const string CacheKey = "meal-analytics";
	private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
	private static readonly TimeSpan FallbackCacheDuration = TimeSpan.FromMinutes(5);
	private static readonly SemaphoreSlim CacheLock = new(1, 1);
	private const string PortalUrl = "https://connecttaylor.atriumcampus.com/index.php";
	private const string DishSiteUrl = "https://dish.avifoodsystems.com/taylor/183/week";

	private readonly DiningHttpFetcher _http = new(httpClient);

	/// <summary>
	/// Gets the latest meal analytics snapshot from the public dining sources.
	/// Falls back to stored Hodson history when live upstream scraping fails.
	/// </summary>
	public async Task<MealAnalyticsResponse> GetAnalyticsAsync(CancellationToken cancellationToken)
	{
		if (cache.TryGetValue(CacheKey, out MealAnalyticsResponse? cached) && cached is not null)
		{
			return cached;
		}

		await CacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (cache.TryGetValue(CacheKey, out cached) && cached is not null)
			{
				return cached;
			}

			try
			{
				MealAnalyticsResponse result = await BuildAnalyticsAsync(cancellationToken).ConfigureAwait(false);
				_ = cache.Set(CacheKey, result, CacheDuration);
				return result;
			}
			catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
			{
				MealAnalyticsResponse? fallback = await TryBuildHistoryFallbackAsync(cancellationToken)
					.ConfigureAwait(false);
				if (fallback is not null)
				{
					logger.LogWarning(
						exception,
						"Live dining scrape failed; serving {DayCount} stored Hodson menu day(s) instead",
						fallback.DailyMenus.Count);
					_ = cache.Set(CacheKey, fallback, FallbackCacheDuration);
					return fallback;
				}

				throw;
			}
		}
		finally
		{
			_ = CacheLock.Release();
		}
	}

	/// <summary>
	/// Scrapes current catalogs and force-backfills as many Dish weeks as upstream still serves.
	/// </summary>
	public Task<MealAnalyticsResponse> FillDatabaseAsync(CancellationToken cancellationToken) =>
		BuildAnalyticsAsync(cancellationToken, forceHistoryBackfill: true);

	private async Task<MealAnalyticsResponse?> TryBuildHistoryFallbackAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<DailyMenu> storedMenus;
		try
		{
			storedMenus = await historyStore.GetStoredDailyMenusAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogError(exception, "Failed to read stored meal history for fallback");
			return null;
		}

		if (storedMenus.Count == 0)
		{
			return null;
		}

		return CreateAnalyticsResponse(
			PortalUrl,
			"https://aviserves.com/taylor/meal-plans-and-dining.html",
			"https://tayloru.catertrax.com/menugrid.asp?mode=aff",
			[],
			storedMenus);
	}

	private async Task<MealAnalyticsResponse> BuildAnalyticsAsync(
		CancellationToken cancellationToken,
		bool forceHistoryBackfill = false)
	{
		DishMenuClient dishMenuClient = new(_http);
		string portalHtml = await _http.GetHtmlAsync(PortalUrl, cancellationToken).ConfigureAwait(false);
		string diningUrl = CaterTraxMenuScraper.ExtractFirstAbsoluteUrl(portalHtml, "aviserves.com")
			?? throw new InvalidOperationException("The Taylor dining page link could not be found.");

		string diningHtml = await _http.GetHtmlAsync(diningUrl, cancellationToken).ConfigureAwait(false);
		string caterTraxBaseUrl = CaterTraxMenuScraper.ExtractFirstAbsoluteUrl(diningHtml, "catertrax.com")
			?? throw new InvalidOperationException("The catering menu link could not be found.");
		string dishUrl = CaterTraxMenuScraper.ExtractFirstAbsoluteUrl(diningHtml, "dish.avifoodsystems.com") ?? DishSiteUrl;

		string menuUrl = new Uri(new Uri(caterTraxBaseUrl), "menugrid.asp?mode=aff").ToString();
		string menuHtml = await _http.GetHtmlAsync(menuUrl, cancellationToken).ConfigureAwait(false);
		List<CaterTraxMenuScraper.CategoryLink> categories = CaterTraxMenuScraper.ExtractCategories(menuHtml, caterTraxBaseUrl);

		if (categories.Count == 0)
		{
			throw new InvalidOperationException("No meal categories were found in the public menu.");
		}

		List<MealItem> meals = [];
		foreach (CaterTraxMenuScraper.CategoryLink category in categories)
		{
			cancellationToken.ThrowIfCancellationRequested();

			string categoryHtml = await _http.GetHtmlAsync(category.Url, cancellationToken).ConfigureAwait(false);
			meals.AddRange(CaterTraxMenuScraper.ExtractMeals(category.Name, categoryHtml, caterTraxBaseUrl));
		}

		if (meals.Count == 0)
		{
			throw new InvalidOperationException("No meals were found in the public menu.");
		}

		(int LocationId, IReadOnlyList<DishMenuClient.DishMealPeriod> MealPeriods)? dishContext = await dishMenuClient
			.ResolveLocationAndMealPeriodsAsync(dishUrl, cancellationToken)
			.ConfigureAwait(false);
		int? locationId = dishContext?.LocationId;
		IReadOnlyList<DishMenuClient.DishMealPeriod>? mealPeriods = dishContext?.MealPeriods;

		try
		{
			await historyStore.EnsureBackfillAsync(
				(anchor, token) => locationId is null || mealPeriods is null || mealPeriods.Count == 0
					? Task.FromResult<IReadOnlyList<DailyMenu>>([])
					: dishMenuClient.BuildDailyMenusForDateAsync(anchor, locationId.Value, mealPeriods, token),
				locationId,
				cancellationToken,
				force: forceHistoryBackfill,
				maxWeeks: forceHistoryBackfill ? 104 : 12).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// History backfill is best-effort; live scrape should still succeed.
		}

		IReadOnlyList<DailyMenu> liveDailyMenus = await dishMenuClient
			.BuildDailyMenusAsync(dishUrl, cancellationToken)
			.ConfigureAwait(false);

		IReadOnlyList<DailyMenu> dailyMenus = liveDailyMenus;
		try
		{
			await historyStore.UpsertDailyMenusAsync(liveDailyMenus, locationId, cancellationToken).ConfigureAwait(false);

			IReadOnlyList<DailyMenu> storedMenus = await historyStore
				.GetStoredDailyMenusAsync(cancellationToken)
				.ConfigureAwait(false);
			dailyMenus = MealHistoryStore.MergeDailyMenus(liveDailyMenus, storedMenus);

			await historyStore.RecordScrapeRunAsync(
				PortalUrl,
				diningUrl,
				menuUrl,
				dailyMenus.Count,
				meals.Count,
				"ok",
				cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			dailyMenus = MealMenuAggregator.DeduplicateDailyMenus(liveDailyMenus);
		}

		return CreateAnalyticsResponse(PortalUrl, diningUrl, menuUrl, meals, dailyMenus);
	}

	private static MealAnalyticsResponse CreateAnalyticsResponse(
		string portalUrl,
		string diningUrl,
		string menuUrl,
		IReadOnlyList<MealItem> meals,
		IReadOnlyList<DailyMenu> dailyMenus)
	{
		DailyMenu[] normalizedMenus = [.. MealMenuAggregator.DeduplicateDailyMenus(dailyMenus)];
		MealItem[] orderedMeals = [.. meals
			.OrderBy(meal => meal.Category)
			.ThenBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)];

		return new MealAnalyticsResponse(
			portalUrl,
			diningUrl,
			menuUrl,
			DateTimeOffset.UtcNow,
			MealAnalyticsBuilder.BuildSummary(orderedMeals),
			orderedMeals,
			MealAnalyticsBuilder.BuildHeatmaps(orderedMeals, normalizedMenus),
			MealAnalyticsBuilder.BuildPredictions(orderedMeals, normalizedMenus),
			MealAnalyticsBuilder.BuildUnannouncedMealPredictions(orderedMeals, normalizedMenus),
			normalizedMenus,
			MealAnalyticsBuilder.BuildMealOccurrences(orderedMeals, normalizedMenus));
	}
}
