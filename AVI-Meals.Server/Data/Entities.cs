namespace AVI_Meals.Server.Data;

public sealed class MenuDayEntity
{
	public int Id { get; set; }
	public DateOnly Date { get; set; }
	public int? LocationId { get; set; }
	public DateTimeOffset UpdatedAtUtc { get; set; }

	public List<MenuItemEntity> Items { get; set; } = [];
}

public sealed class MenuItemEntity
{
	public int Id { get; set; }
	public int MenuDayId { get; set; }
	public MenuDayEntity MenuDay { get; set; } = null!;
	public string MealName { get; set; } = string.Empty;
	public string Station { get; set; } = string.Empty;
	public string MealType { get; set; } = "Unknown";
	public string Category { get; set; } = string.Empty;
	public decimal? Price { get; set; }
	public string TagsJson { get; set; } = "[]";
}

public sealed class CatalogMealEntity
{
	public int Id { get; set; }
	public string Category { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public decimal Price { get; set; }
	public string ProductUrl { get; set; } = string.Empty;
	public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ScrapeRunEntity
{
	public int Id { get; set; }
	public DateTimeOffset RetrievedAtUtc { get; set; }
	public string PortalUrl { get; set; } = string.Empty;
	public string DiningUrl { get; set; } = string.Empty;
	public string MenuUrl { get; set; } = string.Empty;
	public string Status { get; set; } = "ok";
	public int DailyMenuCount { get; set; }
	public int CatalogMealCount { get; set; }
}
