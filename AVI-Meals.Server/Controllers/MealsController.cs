using AVI_Meals.Server.Models;
using AVI_Meals.Server.Services;

using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AVI_Meals.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("api")]
[EnableCors("ClientCors")]
public sealed class MealsController(MealAnalyticsService mealAnalyticsService) : ControllerBase
{
	/// <summary>
	/// Gets the current meal snapshot, heatmaps, and predictions.
	/// </summary>
	[HttpGet]
	[ProducesResponseType<MealAnalyticsResponse>(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status502BadGateway)]
	[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
	public async Task<ActionResult<MealAnalyticsResponse>> GetAsync(CancellationToken cancellationToken)
	{
		try
		{
			MealAnalyticsResponse analytics = await mealAnalyticsService
				.GetAnalyticsAsync(cancellationToken)
				.ConfigureAwait(false);
			return Ok(analytics);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (HttpRequestException)
		{
			return Problem(
				detail: "Upstream dining sources are unavailable.",
				statusCode: StatusCodes.Status503ServiceUnavailable);
		}
		catch (InvalidOperationException exception)
		{
			return Problem(
				detail: exception.Message,
				statusCode: StatusCodes.Status502BadGateway);
		}
	}
}
