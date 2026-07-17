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
	public void DeduplicateDailyMenus_CombinesSameItemAndUnionsTags()
	{
		DailyMenu day = new(
			new DateOnly(2026, 1, 5),
			[
				new DailyMenuItem("Veggie Bowl", DiningStation.MainLine, MealType.Lunch, "Entree", ["Vegan"]),
				new DailyMenuItem("veggie bowl", DiningStation.MainLine, MealType.Lunch, "Entree", ["Contains Soy"]),
				new DailyMenuItem("Veggie Bowl", DiningStation.Grill, MealType.Lunch, "Grill", [])
			]);

		IReadOnlyList<DailyMenu> deduped = MealMenuAggregator.DeduplicateDailyMenus([day]);

		Assert.Equal(2, deduped[0].Items.Count);
		DailyMenuItem combined = Assert.Single(
			deduped[0].Items,
			item => item.Station == DiningStation.MainLine);
		Assert.Equal("Veggie Bowl", combined.MealName);
		Assert.Contains("Vegan", combined.Tags);
		Assert.Contains("Contains Soy", combined.Tags);
	}

	[Fact]
	public void BuildOccurrences_CombinesDuplicatesWithTotalListingCount()
	{
		IReadOnlyList<DailyMenu> menus =
		[
			new DailyMenu(
				new DateOnly(2026, 1, 5),
				[
					new DailyMenuItem("Veggie Bowl", DiningStation.MainLine, MealType.Lunch, "Entree", []),
					new DailyMenuItem("Veggie Bowl", DiningStation.Grill, MealType.Dinner, "Grill", [])
				]),
			new DailyMenu(
				new DateOnly(2026, 1, 6),
				[
					new DailyMenuItem("veggie bowl", DiningStation.MainLine, MealType.Lunch, "Entree", []),
					new DailyMenuItem("Herb Chicken", DiningStation.Homestyle, MealType.Dinner, "Homestyle", [])
				])
		];

		IReadOnlyList<MealOccurrence> occurrences = MealMenuAggregator.BuildOccurrences(menus);

		Assert.Equal(2, occurrences.Count);
		MealOccurrence veggie = Assert.Single(occurrences, item => item.MealName == "Veggie Bowl");
		Assert.Equal(3, veggie.OccurrenceCount);
		Assert.Equal([MealType.Lunch, MealType.Dinner], veggie.MealTypes);
		Assert.Equal(1, occurrences.Single(item => item.MealName == "Herb Chicken").OccurrenceCount);
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
