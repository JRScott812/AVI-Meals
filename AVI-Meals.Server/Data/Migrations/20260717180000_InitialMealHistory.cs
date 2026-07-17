using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AVI_Meals.Server.Data.Migrations;

/// <inheritdoc />
public partial class InitialMealHistory : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "catalog_meals",
			columns: table => new
			{
				Id = table.Column<int>(type: "integer", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				Category = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
				Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
				Description = table.Column<string>(type: "text", nullable: false),
				Price = table.Column<decimal>(type: "numeric", nullable: false),
				PriceLabel = table.Column<string>(type: "text", nullable: false),
				ProductUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
				KeywordsJson = table.Column<string>(type: "text", nullable: false),
				UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_catalog_meals", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "menu_days",
			columns: table => new
			{
				Id = table.Column<int>(type: "integer", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				Date = table.Column<DateOnly>(type: "date", nullable: false),
				LocationId = table.Column<int>(type: "integer", nullable: true),
				UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_menu_days", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "scrape_runs",
			columns: table => new
			{
				Id = table.Column<int>(type: "integer", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				RetrievedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				PortalUrl = table.Column<string>(type: "text", nullable: false),
				DiningUrl = table.Column<string>(type: "text", nullable: false),
				MenuUrl = table.Column<string>(type: "text", nullable: false),
				Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
				DailyMenuCount = table.Column<int>(type: "integer", nullable: false),
				CatalogMealCount = table.Column<int>(type: "integer", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_scrape_runs", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "menu_items",
			columns: table => new
			{
				Id = table.Column<int>(type: "integer", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				MenuDayId = table.Column<int>(type: "integer", nullable: false),
				MealName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
				Station = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
				Category = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
				Price = table.Column<decimal>(type: "numeric", nullable: true),
				TagsJson = table.Column<string>(type: "text", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_menu_items", x => x.Id);
				table.ForeignKey(
					name: "FK_menu_items_menu_days_MenuDayId",
					column: x => x.MenuDayId,
					principalTable: "menu_days",
					principalColumn: "Id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.CreateIndex(
			name: "IX_catalog_meals_ProductUrl",
			table: "catalog_meals",
			column: "ProductUrl",
			unique: true);

		migrationBuilder.CreateIndex(
			name: "IX_menu_days_Date",
			table: "menu_days",
			column: "Date",
			unique: true);

		migrationBuilder.CreateIndex(
			name: "IX_menu_items_MenuDayId_MealName_Station",
			table: "menu_items",
			columns: new[] { "MenuDayId", "MealName", "Station" },
			unique: true);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(name: "catalog_meals");
		migrationBuilder.DropTable(name: "menu_items");
		migrationBuilder.DropTable(name: "scrape_runs");
		migrationBuilder.DropTable(name: "menu_days");
	}
}
