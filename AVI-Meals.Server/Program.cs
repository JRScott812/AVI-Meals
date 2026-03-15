namespace AVI_Meals.Server
{
	public class Program
	{
		public static void Main(string[] args)
		{
			WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

			// Add services to the container.
			builder.Services.AddMemoryCache();
			builder.Services.AddHttpClient<Services.MealAnalyticsService>(client =>
			{
				client.Timeout = TimeSpan.FromSeconds(30);
				client.DefaultRequestHeaders.UserAgent.ParseAdd("AVI-Meals/1.0");
			});
			builder.Services.AddControllers();
			// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
			builder.Services.AddOpenApi();

			WebApplication app = builder.Build();

			app.UseDefaultFiles();
			app.MapStaticAssets();

			// Configure the HTTP request pipeline.
			if (app.Environment.IsDevelopment())
			{
				app.MapOpenApi();
			}

			app.UseHttpsRedirection();

			app.UseAuthorization();


			app.MapControllers();

			app.MapFallbackToFile("/index.html");

			app.Run();
		}
	}
}