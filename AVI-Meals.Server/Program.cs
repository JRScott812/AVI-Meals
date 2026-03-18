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
			Console.WriteLine($"[CORS] Allowed origins: {string.Join(", ", allowedOrigins)}");
			builder.Services.AddCors(options =>
			{
				options.AddPolicy(ClientCorsPolicyName, policyBuilder =>
				{
					// Always apply CORS policy, even if only one origin is set
					if (allowedOrigins.Length > 0)
					{
						policyBuilder
							.WithOrigins(allowedOrigins)
							.AllowAnyHeader()
							.AllowAnyMethod();
					}
					else
					{
						// Allow all origins if none are configured (for debugging only)
						policyBuilder
							.AllowAnyOrigin()
							.AllowAnyHeader()
							.AllowAnyMethod();
					}
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
			app.MapGet("/health", () =>
			{
				long memBytes = GC.GetTotalMemory(forceFullCollection: false);
				string memMB = $"Memory usage: {memBytes / (1024 * 1024)} MB";
				Console.WriteLine($"[HEALTH] {memMB}");
				return Results.Ok(memMB);
			});
			// Log memory usage at startup
			long startupMemBytes = GC.GetTotalMemory(forceFullCollection: false);
			Console.WriteLine($"[STARTUP] Memory usage: {startupMemBytes / (1024 * 1024)} MB");
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