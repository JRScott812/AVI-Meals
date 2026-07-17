using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AVI_Meals.Server.Data.Migrations
{
	/// <inheritdoc />
	public partial class HodsonOnlyNoPrice : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(
				name: "catalog_meals");

			migrationBuilder.DropColumn(
				name: "Price",
				table: "menu_items");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AddColumn<decimal>(
				name: "Price",
				table: "menu_items",
				type: "numeric",
				nullable: true);

			migrationBuilder.CreateTable(
				name: "catalog_meals",
				columns: table => new
				{
					Id = table.Column<int>(type: "integer", nullable: false)
						.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
					Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
					Description = table.Column<string>(type: "text", nullable: false),
					Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
					Price = table.Column<decimal>(type: "numeric", nullable: false),
					ProductUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
					UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_catalog_meals", x => x.Id);
				});

			migrationBuilder.CreateIndex(
				name: "IX_catalog_meals_ProductUrl",
				table: "catalog_meals",
				column: "ProductUrl",
				unique: true);
		}
	}
}
