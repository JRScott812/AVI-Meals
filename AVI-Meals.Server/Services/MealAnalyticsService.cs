using AVI_Meals.Server.Models;

using Microsoft.Extensions.Caching.Memory;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Orchestrates dining-source fetches, optional history persistence, and analytics.
/// </summary>
public sealed class MealAnalyticsService(
	HttpClient httpClient,
	IMemoryCache cache,
	MealHistoryStore historyStore)
{
	private const string CacheKey = "meal-analytics";
	private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
	private static readonly SemaphoreSlim CacheLock = new(1, 1);
	private const string PortalUrl = "https://connecttaylor.atriumcampus.com/index.php";
	private const string DishSiteUrl = "https://dish.avifoodsystems.com/taylor/183/week";

	private readonly DiningHttpFetcher _http = new(httpClient);

	/// <summary>
	/// Gets the latest meal analytics snapshot from the public dining sources.
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

			MealAnalyticsResponse result = await BuildAnalyticsAsync(cancellationToken).ConfigureAwait(false);
			_ = cache.Set(CacheKey, result, CacheDuration);
			return result;
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

		(int LocationId, IReadOnlyList<int> MealIds)? dishContext = await dishMenuClient
			.ResolveLocationAndMealIdsAsync(dishUrl, cancellationToken)
			.ConfigureAwait(false);
		int? locationId = dishContext?.LocationId;
		IReadOnlyList<int>? mealIds = dishContext?.MealIds;

		try
		{
			await historyStore.EnsureBackfillAsync(
				(anchor, token) => locationId is null || mealIds is null || mealIds.Count == 0
					? Task.FromResult<IReadOnlyList<DailyMenu>>([])
					: dishMenuClient.BuildDailyMenusForDateAsync(anchor, locationId.Value, mealIds, token),
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
			await historyStore.UpsertCatalogMealsAsync(meals, cancellationToken).ConfigureAwait(false);

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
			dailyMenus = liveDailyMenus;
		}

		MealItem[] orderedMeals = [.. meals
			.OrderBy(meal => meal.Category, StringComparer.OrdinalIgnoreCase)
			.ThenBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)];

		return new MealAnalyticsResponse(
			PortalUrl,
			diningUrl,
			menuUrl,
			DateTimeOffset.UtcNow,
			MealAnalyticsBuilder.BuildSummary(orderedMeals),
			orderedMeals,
			MealAnalyticsBuilder.BuildHeatmaps(orderedMeals),
			MealAnalyticsBuilder.BuildPredictions(orderedMeals),
			MealAnalyticsBuilder.BuildUnannouncedMealPredictions(orderedMeals),
			dailyMenus,
			MealAnalyticsBuilder.BuildMealOccurrences(orderedMeals, dailyMenus));
	}
}
