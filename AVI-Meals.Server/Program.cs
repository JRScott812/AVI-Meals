using System.Threading.RateLimiting;

using AVI_Meals.Server.Data;
using AVI_Meals.Server.Models;
using AVI_Meals.Server.Services;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

namespace AVI_Meals.Server;

public class Program
{
	private const string ClientCorsPolicyName = "ClientCors";
	private const string ApiRateLimitPolicyName = "api";
	private const long MaxOutboundResponseBytes = 2 * 1024 * 1024;

	public static async Task Main(string[] args)
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
		ILogger startupLogger = LoggerFactory
			.Create(logging => logging.AddConsole())
			.CreateLogger("Startup");

		_ = builder.Services.Configure<ForwardedHeadersOptions>(options =>
		{
			// Heroku terminates TLS at the router and forwards proto/for headers.
			// Known proxy lists are cleared because dyno egress IPs are not fixed.
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			options.KnownIPNetworks.Clear();
			options.KnownProxies.Clear();
		});

		string[] allowedOrigins = GetCorsAllowedOrigins(builder.Configuration);
		bool isDevelopment = builder.Environment.IsDevelopment();
		if (allowedOrigins.Length == 0 && !isDevelopment)
		{
			throw new InvalidOperationException(
				"Cors:AllowedOrigins must be configured in non-Development environments.");
		}

		startupLogger.LogInformation(
			"CORS allowed origins: {Origins}",
			allowedOrigins.Length == 0 ? "(development fallback: any)" : string.Join(", ", allowedOrigins));

		_ = builder.Services.AddCors(options =>
		{
			options.AddPolicy(ClientCorsPolicyName, policyBuilder =>
			{
				if (allowedOrigins.Length > 0)
				{
					_ = policyBuilder
						.WithOrigins(allowedOrigins)
						.WithHeaders("Accept", "Content-Type")
						.WithMethods("GET", "HEAD", "OPTIONS");
					return;
				}

				// Development-only fallback when origins are not configured.
				_ = policyBuilder
					.AllowAnyOrigin()
					.WithHeaders("Accept", "Content-Type")
					.WithMethods("GET", "HEAD", "OPTIONS");
			});
		});

		_ = builder.Services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			_ = options.AddPolicy(ApiRateLimitPolicyName, httpContext =>
				RateLimitPartition.GetFixedWindowLimiter(
					httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
					_ => new FixedWindowRateLimiterOptions
					{
						PermitLimit = 60,
						Window = TimeSpan.FromMinutes(1),
						QueueLimit = 0
					}));
		});

		string? connectionString = builder.Configuration.GetValue("MealHistory:Disabled", false)
			? null
			: DatabaseConnection.Resolve(builder.Configuration);
		if (!string.IsNullOrWhiteSpace(connectionString))
		{
			_ = builder.Services.AddDbContext<MealsDbContext>(options =>
				options.UseNpgsql(connectionString));
			startupLogger.LogInformation("Meal history database is configured");
		}
		else
		{
			startupLogger.LogWarning(
				"No ConnectionStrings:DefaultConnection or DATABASE_URL set; meal history persistence is disabled");
		}

		_ = builder.Services.AddSingleton<MealHistoryStore>();
		_ = builder.Services.AddMemoryCache();
		_ = builder.Services.AddTransient<SafeOutboundHandler>();
		_ = builder.Services.AddHttpClient<MealAnalyticsService>(client =>
			{
				client.Timeout = TimeSpan.FromSeconds(120);
				client.MaxResponseContentBufferSize = MaxOutboundResponseBytes;
				client.DefaultRequestHeaders.UserAgent.ParseAdd("AVI-Meals/1.0");
			})
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
			{
				AllowAutoRedirect = false,
				MaxAutomaticRedirections = 3
			})
			.AddHttpMessageHandler<SafeOutboundHandler>();

		_ = builder.Services.AddProblemDetails();
		_ = builder.Services.AddControllers();
		_ = builder.Services.AddOpenApi();

		WebApplication app = builder.Build();
		ILogger logger = app.Logger;

		if (!string.IsNullOrWhiteSpace(connectionString))
		{
			try
			{
				using IServiceScope scope = app.Services.CreateScope();
				MealsDbContext db = scope.ServiceProvider.GetRequiredService<MealsDbContext>();
				db.Database.Migrate();
				logger.LogInformation("Applied meal history database migrations");
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				// Keep the API available even if history storage is temporarily unreachable.
				logger.LogError(exception, "Failed to apply meal history migrations; continuing without DB history");
			}
		}

		if (args.Any(argument => string.Equals(argument, "--backfill", StringComparison.OrdinalIgnoreCase)))
		{
			logger.LogInformation("Running forced meal history backfill");
			MealAnalyticsService analytics = app.Services.GetRequiredService<MealAnalyticsService>();
			MealAnalyticsResponse filled = await analytics
				.FillDatabaseAsync(CancellationToken.None)
				.ConfigureAwait(false);
			logger.LogInformation(
				"Backfill complete: {CatalogCount} catalog meals, {DayCount} menu days, {OccurrenceCount} occurrence rows",
				filled.Meals.Count,
				filled.DailyMenus.Count,
				filled.MealOccurrences.Count);
			return;
		}

		_ = app.UseForwardedHeaders();
		_ = app.Use(async (context, next) =>
		{
			_ = context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
			_ = context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
			_ = context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
			_ = context.Response.Headers.TryAdd("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
			await next().ConfigureAwait(false);
		});

		// CORS must run before HTTPS redirection / endpoints so browser preflights and
		// error responses from this app still include Access-Control-Allow-Origin.
		_ = app.UseCors(ClientCorsPolicyName);

		_ = app.UseDefaultFiles();
		_ = app.MapStaticAssets();

		if (app.Environment.IsDevelopment())
		{
			_ = app.MapOpenApi();
		}
		else
		{
			_ = app.UseHsts();
		}

		// Platform proxies (Heroku / Azure App Service) terminate TLS and forward HTTP to the app.
		// Skipping HTTPS redirection avoids redirect loops and broken health probes.
		bool behindPlatformProxy =
			!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DYNO"))
			|| !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
		if (!behindPlatformProxy)
		{
			_ = app.UseHttpsRedirection();
		}

		_ = app.UseRateLimiter();
		_ = app.MapControllers().RequireCors(ClientCorsPolicyName);
		_ = app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
			.RequireCors(ClientCorsPolicyName)
			.RequireRateLimiting(ApiRateLimitPolicyName);

		logger.LogInformation("AVI-Meals server starting");
		_ = app.MapFallbackToFile("/index.html");
		await app.RunAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Reads explicitly allowed client origins for cross-origin API calls.
	/// </summary>
	private static string[] GetCorsAllowedOrigins(ConfigurationManager configuration)
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
