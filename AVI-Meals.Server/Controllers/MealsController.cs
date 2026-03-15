using AVI_Meals.Server.Models;
using AVI_Meals.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace AVI_Meals.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MealsController(MealAnalyticsService mealAnalyticsService) : ControllerBase
{
	/// <summary>
	/// Gets the current meal snapshot, heatmaps, and predictions.
	/// </summary>
	[HttpGet]
	[ProducesResponseType<MealAnalyticsResponse>(StatusCodes.Status200OK)]
	public Task<MealAnalyticsResponse> GetAsync(CancellationToken cancellationToken)
	{
		return mealAnalyticsService.GetAnalyticsAsync(cancellationToken);
	}
}