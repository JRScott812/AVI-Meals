using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AVI_Meals.Server.Data.Migrations
{
	/// <inheritdoc />
	public partial class MealTaxonomyEnums : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropIndex(
				name: "IX_menu_items_MenuDayId_MealName_Station",
				table: "menu_items");

			migrationBuilder.DropColumn(
				name: "KeywordsJson",
				table: "catalog_meals");

			migrationBuilder.DropColumn(
				name: "PriceLabel",
				table: "catalog_meals");

			migrationBuilder.AlterColumn<string>(
				name: "Station",
				table: "menu_items",
				type: "character varying(64)",
				maxLength: 64,
				nullable: false,
				oldClrType: typeof(string),
				oldType: "character varying(256)",
				oldMaxLength: 256);

			migrationBuilder.AddColumn<string>(
				name: "MealType",
				table: "menu_items",
				type: "character varying(32)",
				maxLength: 32,
				nullable: false,
				defaultValue: "Unknown");

			migrationBuilder.AlterColumn<string>(
				name: "Category",
				table: "catalog_meals",
				type: "character varying(64)",
				maxLength: 64,
				nullable: false,
				oldClrType: typeof(string),
				oldType: "character varying(256)",
				oldMaxLength: 256);

			migrationBuilder.CreateIndex(
				name: "IX_menu_items_MenuDayId_MealName_Station_MealType",
				table: "menu_items",
				columns: new[] { "MenuDayId", "MealName", "Station", "MealType" },
				unique: true);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropIndex(
				name: "IX_menu_items_MenuDayId_MealName_Station_MealType",
				table: "menu_items");

			migrationBuilder.DropColumn(
				name: "MealType",
				table: "menu_items");

			migrationBuilder.AlterColumn<string>(
				name: "Station",
				table: "menu_items",
				type: "character varying(256)",
				maxLength: 256,
				nullable: false,
				oldClrType: typeof(string),
				oldType: "character varying(64)",
				oldMaxLength: 64);

			migrationBuilder.AlterColumn<string>(
				name: "Category",
				table: "catalog_meals",
				type: "character varying(256)",
				maxLength: 256,
				nullable: false,
				oldClrType: typeof(string),
				oldType: "character varying(64)",
				oldMaxLength: 64);

			migrationBuilder.AddColumn<string>(
				name: "KeywordsJson",
				table: "catalog_meals",
				type: "text",
				nullable: false,
				defaultValue: "");

			migrationBuilder.AddColumn<string>(
				name: "PriceLabel",
				table: "catalog_meals",
				type: "text",
				nullable: false,
				defaultValue: "");

			migrationBuilder.CreateIndex(
				name: "IX_menu_items_MenuDayId_MealName_Station",
				table: "menu_items",
				columns: new[] { "MenuDayId", "MealName", "Station" },
				unique: true);
		}
	}
}
