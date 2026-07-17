using AVI_Meals.Server.Data;
using AVI_Meals.Server.Models;
using AVI_Meals.Server.Services;

using Microsoft.Extensions.Configuration;

using Xunit;

namespace AVI_Meals.Server.Tests;

public sealed class MealHistoryStoreTests
{
	[Fact]
	public void MergeDailyMenus_PrefersLiveDayOverStored()
	{
		DailyMenu stored = new(new DateOnly(2026, 1, 5), [new DailyMenuItem("Old", DiningStation.Trattoria, MealType.Lunch, "Pasta", [])]);
		DailyMenu live = new(new DateOnly(2026, 1, 5), [new DailyMenuItem("New", DiningStation.Grill, MealType.Dinner, "Grill", [])]);
		DailyMenu older = new(new DateOnly(2026, 1, 1), [new DailyMenuItem("Past", DiningStation.Homestyle, MealType.Breakfast, "Breakfast", [])]);

		IReadOnlyList<DailyMenu> merged = MealHistoryStore.MergeDailyMenus([live], [stored, older]);

		Assert.Equal(2, merged.Count);
		Assert.Equal(new DateOnly(2026, 1, 1), merged[0].Date);
		Assert.Equal(new DateOnly(2026, 1, 5), merged[1].Date);
		Assert.Equal("New", merged[1].Items[0].MealName);
	}

	[Fact]
	public void DatabaseConnection_NormalizesPostgresUri()
	{
		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["DATABASE_URL"] = "postgresql://user:p%40ss@ep-example.neon.tech/neondb?sslmode=require&channel_binding=require"
			})
			.Build();

		string? connection = DatabaseConnection.Resolve(configuration);

		Assert.NotNull(connection);
		Assert.Contains("Host=ep-example.neon.tech", connection, StringComparison.Ordinal);
		Assert.Contains("Username=user", connection, StringComparison.Ordinal);
		Assert.Contains("Password=p@ss", connection, StringComparison.Ordinal);
		Assert.Contains("Database=neondb", connection, StringComparison.Ordinal);
		Assert.Contains("SSL Mode=Require", connection, StringComparison.Ordinal);
		Assert.Contains("Channel Binding=Require", connection, StringComparison.Ordinal);
	}
}
