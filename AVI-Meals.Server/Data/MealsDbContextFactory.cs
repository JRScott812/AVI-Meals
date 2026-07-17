using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AVI_Meals.Server.Data;

/// <summary>
/// Design-time factory for EF Core migrations. Prefers env/user-secrets over a local placeholder.
/// </summary>
public sealed class MealsDbContextFactory : IDesignTimeDbContextFactory<MealsDbContext>
{
	public MealsDbContext CreateDbContext(string[] args)
	{
		string basePath = Directory.GetCurrentDirectory();
		if (!File.Exists(Path.Combine(basePath, "AVI-Meals.Server.csproj")))
		{
			basePath = Path.Combine(basePath, "AVI-Meals.Server");
		}

		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(basePath)
			.AddJsonFile("appsettings.json", optional: true)
			.AddJsonFile("appsettings.Development.json", optional: true)
			.AddEnvironmentVariables()
			.AddUserSecrets(typeof(MealsDbContextFactory).Assembly, optional: true)
			.Build();

		string connectionString = DatabaseConnection.Resolve(configuration)
			?? "Host=localhost;Database=avi_meals;Username=postgres;Password=postgres";

		DbContextOptionsBuilder<MealsDbContext> optionsBuilder = new();
		_ = optionsBuilder.UseNpgsql(connectionString);
		return new MealsDbContext(optionsBuilder.Options);
	}
}
