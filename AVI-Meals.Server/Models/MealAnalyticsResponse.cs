using System.Text.Json.Serialization;

namespace AVI_Meals.Server.Models;

public sealed record MealAnalyticsResponse(
	string PortalUrl,
	string DiningUrl,
	string MenuUrl,
	DateTimeOffset RetrievedAtUtc,
	MealSummary Summary,
	IReadOnlyList<MealItem> Meals,
	IReadOnlyList<Heatmap> Heatmaps,
	IReadOnlyList<Prediction> Predictions,
	IReadOnlyList<UnannouncedMealPrediction> UnannouncedMealPredictions,
	IReadOnlyList<DailyMenu> DailyMenus,
	IReadOnlyList<MealOccurrence> MealOccurrences);

public sealed record MealSummary(
	int MealCount,
	int CategoryCount,
	decimal LowestPrice,
	decimal HighestPrice,
	decimal AveragePrice,
	IReadOnlyList<CateringCategory> Categories);

public sealed record MealItem(
	CateringCategory Category,
	string Name,
	string Description,
	decimal Price,
	string ProductUrl)
{
	/// <summary>
	/// Keywords derived from <see cref="Name"/> and <see cref="Description"/>.
	/// </summary>
	[JsonInclude]
	public IReadOnlyList<string> Keywords => MealKeywords.Extract(Name, Description);
}

public sealed record Heatmap(
	string Title,
	IReadOnlyList<string> Columns,
	IReadOnlyList<HeatmapRow> Rows);

public sealed record HeatmapRow(
	string Label,
	IReadOnlyList<HeatmapCell> Cells);

public sealed record HeatmapCell(
	string Label,
	int Value,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Bucket);

public sealed record Prediction(
	string Title,
	string Detail,
	decimal Confidence);

public sealed record UnannouncedMealPrediction(
	string Name,
	CateringCategory Category,
	string Rationale,
	decimal PredictedPrice,
	decimal Confidence,
	IReadOnlyList<string> Keywords);

public sealed record DailyMenu(
	DateOnly Date,
	IReadOnlyList<DailyMenuItem> Items);

public sealed record DailyMenuItem(
	string MealName,
	DiningStation Station,
	MealType MealType,
	string Category,
	decimal? Price,
	IReadOnlyList<string> Tags);

/// <summary>
/// Aggregated meal occurrence count used to identify duplicates.
/// </summary>
public sealed record MealOccurrence(
	string MealName,
	int OccurrenceCount);
