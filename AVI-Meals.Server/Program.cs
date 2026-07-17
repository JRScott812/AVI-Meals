using System.Threading.RateLimiting;

using AVI_Meals.Server.Services;

using Microsoft.AspNetCore.HttpOverrides;

namespace AVI_Meals.Server;

public class Program
{
	private const string ClientCorsPolicyName = "ClientCors";
	private const string ApiRateLimitPolicyName = "api";
	private const long MaxOutboundResponseBytes = 2 * 1024 * 1024;

	public static void Main(string[] args)
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

		_ = builder.Services.AddMemoryCache();
		_ = builder.Services.AddTransient<SafeOutboundHandler>();
		_ = builder.Services.AddHttpClient<MealAnalyticsService>(client =>
			{
				client.Timeout = TimeSpan.FromSeconds(30);
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

		_ = app.UseForwardedHeaders();
		_ = app.Use(async (context, next) =>
		{
			_ = context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
			_ = context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
			_ = context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
			_ = context.Response.Headers.TryAdd("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
			await next().ConfigureAwait(false);
		});
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

		_ = app.UseHttpsRedirection();
		_ = app.UseCors(ClientCorsPolicyName);
		_ = app.UseRateLimiter();
		_ = app.MapControllers();
		_ = app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
			.RequireRateLimiting(ApiRateLimitPolicyName);

		logger.LogInformation("AVI-Meals server starting");
		_ = app.MapFallbackToFile("/index.html");
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
