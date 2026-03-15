using System.Net;
using System.Text;
using AVI_Meals.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
		Assert.Contains("Continental Breakfast Buffet", body, StringComparison.OrdinalIgnoreCase);
	}

	private sealed class TestWebApplicationFactory : WebApplicationFactory<AVI_Meals.Server.Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
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
		<html><body><a href="https://tayloru.catertrax.com/">Catering</a></body></html>
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
				_ => "<html><body>Not Found</body></html>"
			};

			HttpStatusCode statusCode = payload.Contains("Not Found", StringComparison.Ordinal)
				? HttpStatusCode.NotFound
				: HttpStatusCode.OK;

			return Task.FromResult(new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(payload, Encoding.UTF8, "text/html")
			});
		}
	}
}
