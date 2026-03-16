using System.Net;
using System.Text;
using AVI_Meals.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AVI_Meals.Server.Tests;

public sealed class MealsEndpointTests
{
	[Fact]
	public async Task GetMeals_ReturnsAnalyticsPayload()
	{
		await using TestWebApplicationFactory factory = new();
		using HttpClient client = factory.CreateClient();

		using HttpResponseMessage response = await client.GetAsync("/api/meals");
		string body = await response.Content.ReadAsStringAsync();

		if (response.StatusCode != HttpStatusCode.OK)
		{
			Assert.True(false, $"Expected 200 but got {(int)response.StatusCode}: {body}");
		}

		Assert.Contains("\"summary\"", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("\"meals\"", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("\"heatmaps\"", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("\"predictions\"", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("\"unannouncedMealPredictions\"", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("\"dailyMenus\"", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("\"mealOccurrences\"", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Continental Breakfast Buffet", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Monday Veggie Bowl", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("\"mealName\":\"Monday Veggie Bowl\"", body, StringComparison.Ordinal);
		Assert.Contains("\"occurrenceCount\":2", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetMeals_AddsCorsHeaders_WhenOriginIsAllowed()
	{
		const string allowedOrigin = "https://jrscott812.github.io";
		await using TestWebApplicationFactory factory = new(allowedOrigin);
		using HttpClient client = factory.CreateClient();

		HttpRequestMessage request = new(HttpMethod.Get, "/api/meals");
		request.Headers.TryAddWithoutValidation("Origin", allowedOrigin);
		using HttpResponseMessage response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? originValues));
		Assert.Contains(allowedOrigin, originValues!);
	}

	private sealed class TestWebApplicationFactory(string? allowedOrigin = null) : WebApplicationFactory<AVI_Meals.Server.Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			if (!string.IsNullOrWhiteSpace(allowedOrigin))
			{
				builder.UseSetting("Cors:AllowedOrigins:0", allowedOrigin);
			}

			builder.ConfigureAppConfiguration((_, configBuilder) =>
			{
				if (string.IsNullOrWhiteSpace(allowedOrigin))
				{
					return;
				}

				Dictionary<string, string?> corsSettings = new()
				{
					["Cors:AllowedOrigins:0"] = allowedOrigin
				};
				configBuilder.AddInMemoryCollection(corsSettings);
			});

			builder.ConfigureTestServices(services =>
			{
				services.RemoveAll(typeof(MealAnalyticsService));
				services.AddHttpClient<MealAnalyticsService>()
					.ConfigurePrimaryHttpMessageHandler(() => new StubDiningHttpMessageHandler());
			});
		}
	}

	private sealed class StubDiningHttpMessageHandler : HttpMessageHandler
	{
		private const string PortalHtml = """
		<html><body><a href="https://aviserves.com/taylor/meal-plans-and-dining.html">View Our Menus</a></body></html>
		""";

		private const string DiningHtml = """
		<html><body>
		<a href="https://tayloru.catertrax.com/">Catering</a>
		<a href="https://dish.avifoodsystems.com/taylor/999/4/2026-01-05/week">Dish menus</a>
		</body></html>
		""";

		private const string DishLocationsJson = """
		[
			{ "id": 999, "name": "Taylor Dining Hall" },
			{ "id": 1000, "name": "Other Location" }
		]
		""";

		private const string DishWeeklyMenuJson = """
		[
			{
				"name": "Monday Veggie Bowl",
				"date": "2026-01-05",
				"stationName": "Main Line",
				"categoryName": "Lunch",
				"price": 10.25,
				"preferences": [{ "name": "Vegan" }],
				"allergens": [{ "name": "Soy" }]
			},
			{
				"name": "Tuesday Herb Chicken",
				"date": "2026-01-06",
				"stationName": "Grill",
				"categoryName": "Dinner",
				"price": 12.50,
				"preferences": [],
				"allergens": []
			}
		]
		""";

		private const string MenuHtml = """
		<html><body>
		<div class='category-slot'><a class='category-tile' href='menuGrid.asp?mode=p&cg=3&c=21&a=1&intCustomerID='><div class='tile-title'>Breakfast</div></a></div>
		</body></html>
		""";

		private const string CategoryHtml = """
		<html><body>
		<div class='product-slot'><a class='product-tile' href="product.asp?intCustomerID=&a=1#a:1|c:3|l:21|p:44"><div class='tile-title'>Continental Breakfast Buffet</div><div class='tile-description'><p>Fresh cut fruit and assorted, fresh-baked pastries.</p></div><div class='call-to-action'><div class='price'><div class='cost'>$8.99</div></div></div></a></div>
		<div class='product-slot'><a class='product-tile' href="product.asp?intCustomerID=&a=1#a:1|c:3|l:21|p:45"><div class='tile-title'>Garden Egg Skillet</div><div class='tile-description'><p>Eggs with vegetables and herbs.</p></div><div class='call-to-action'><div class='price'><div class='cost'>$10.99</div></div></div></a></div>
		<div class='product-slot'><a class='product-tile' href="product.asp?intCustomerID=&a=1#a:1|c:3|l:21|p:46"><div class='tile-title'>Monday Veggie Bowl</div><div class='tile-description'><p>Seasonal vegetables with grains.</p></div><div class='call-to-action'><div class='price'><div class='cost'>$10.25</div></div></div></a></div>
		</body></html>
		""";

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.ToString() ?? string.Empty;
			string payload = url switch
			{
				var value when value.Contains("connecttaylor.atriumcampus.com/index.php", StringComparison.OrdinalIgnoreCase) => PortalHtml,
				var value when value.Contains("aviserves.com/taylor/meal-plans-and-dining.html", StringComparison.OrdinalIgnoreCase) => DiningHtml,
				var value when value.Contains("tayloru.catertrax.com/menugrid.asp?mode=aff", StringComparison.OrdinalIgnoreCase) => MenuHtml,
				var value when value.Contains("menuGrid.asp?mode=p&cg=3&c=21", StringComparison.OrdinalIgnoreCase) => CategoryHtml,
				var value when value.Contains("dish.avifoodsystems.com/api/locations?client=taylor", StringComparison.OrdinalIgnoreCase) => DishLocationsJson,
				var value when value.Contains("dish.avifoodsystems.com/api/menu-items/week", StringComparison.OrdinalIgnoreCase) => DishWeeklyMenuJson,
				_ => "<html><body>Not Found</body></html>"
			};

			HttpStatusCode statusCode = payload.Contains("Not Found", StringComparison.Ordinal)
				? HttpStatusCode.NotFound
				: HttpStatusCode.OK;

			return Task.FromResult(new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(payload, Encoding.UTF8, payload.StartsWith("[", StringComparison.Ordinal) ? "application/json" : "text/html")
			});
		}
	}
}
