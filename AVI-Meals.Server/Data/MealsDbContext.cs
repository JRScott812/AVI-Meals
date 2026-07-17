using Microsoft.EntityFrameworkCore;

namespace AVI_Meals.Server.Data;

public sealed class MealsDbContext(DbContextOptions<MealsDbContext> options) : DbContext(options)
{
	public DbSet<MenuDayEntity> MenuDays => Set<MenuDayEntity>();
	public DbSet<MenuItemEntity> MenuItems => Set<MenuItemEntity>();
	public DbSet<CatalogMealEntity> CatalogMeals => Set<CatalogMealEntity>();
	public DbSet<ScrapeRunEntity> ScrapeRuns => Set<ScrapeRunEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<MenuDayEntity>(entity =>
		{
			entity.ToTable("menu_days");
			entity.HasKey(day => day.Id);
			entity.HasIndex(day => day.Date).IsUnique();
			entity.Property(day => day.Date).IsRequired();
		});

		modelBuilder.Entity<MenuItemEntity>(entity =>
		{
			entity.ToTable("menu_items");
			entity.HasKey(item => item.Id);
			entity.Property(item => item.MealName).HasMaxLength(512).IsRequired();
			entity.Property(item => item.Station).HasMaxLength(64).IsRequired();
			entity.Property(item => item.MealType).HasMaxLength(32).IsRequired();
			entity.Property(item => item.Category).HasMaxLength(256).IsRequired();
			entity.Property(item => item.TagsJson).IsRequired();
			entity.HasIndex(item => new { item.MenuDayId, item.MealName, item.Station, item.MealType }).IsUnique();
			entity.HasOne(item => item.MenuDay)
				.WithMany(day => day.Items)
				.HasForeignKey(item => item.MenuDayId)
				.OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<CatalogMealEntity>(entity =>
		{
			entity.ToTable("catalog_meals");
			entity.HasKey(meal => meal.Id);
			entity.Property(meal => meal.Category).HasMaxLength(64).IsRequired();
			entity.Property(meal => meal.Name).HasMaxLength(512).IsRequired();
			entity.Property(meal => meal.ProductUrl).HasMaxLength(2048).IsRequired();
			entity.HasIndex(meal => meal.ProductUrl).IsUnique();
		});

		modelBuilder.Entity<ScrapeRunEntity>(entity =>
		{
			entity.ToTable("scrape_runs");
			entity.HasKey(run => run.Id);
			entity.Property(run => run.Status).HasMaxLength(64).IsRequired();
		});
	}
}
