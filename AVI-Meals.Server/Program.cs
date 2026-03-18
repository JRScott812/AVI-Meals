using Microsoft.AspNetCore.HttpOverrides;

namespace AVI_Meals.Server
{
	public class Program
	{
		private const string ClientCorsPolicyName = "ClientCors";

		public static void Main(string[] args)
		{
			WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

			builder.Services.Configure<ForwardedHeadersOptions>(options =>
			{
				options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
				options.KnownIPNetworks.Clear();
				options.KnownProxies.Clear();
			});

			string[] allowedOrigins = GetCorsAllowedOrigins(builder.Configuration);
			builder.Services.AddCors(options =>
			{
				options.AddPolicy(ClientCorsPolicyName, policyBuilder =>
				{
					if (allowedOrigins.Length == 0)
					{
						return;
					}

					policyBuilder
						.WithOrigins(allowedOrigins)
						.AllowAnyHeader()
						.AllowAnyMethod();
				});
			});

			builder.Services.AddMemoryCache();
			builder.Services.AddHttpClient<Services.MealAnalyticsService>(client =>
			{
				client.Timeout = TimeSpan.FromSeconds(30);
				client.DefaultRequestHeaders.UserAgent.ParseAdd("AVI-Meals/1.0");
			});
			builder.Services.AddControllers();
			builder.Services.AddOpenApi();

			WebApplication app = builder.Build();

			app.UseForwardedHeaders();
			app.UseDefaultFiles();
			app.MapStaticAssets();

			if (app.Environment.IsDevelopment())
			{
				app.MapOpenApi();
			}

			app.UseHttpsRedirection();
			app.UseCors(ClientCorsPolicyName);
			app.UseAuthorization();
			app.MapControllers();
			// Health check endpoint for Heroku
			app.MapGet("/health", () => Results.Ok("Healthy"));
			app.MapFallbackToFile("/index.html");
			app.Run();
		}

		/// <summary>
		/// Reads explicitly allowed client origins for cross-origin API calls.
		/// </summary>
		private static string[] GetCorsAllowedOrigins(IConfiguration configuration)
		{
			return configuration
				.GetSection("Cors:AllowedOrigins")
				.Get<string[]>()?
				.Where(origin => !string.IsNullOrWhiteSpace(origin))
				.Select(origin => origin.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray()
				?? [];
		}
	}
}