using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rackline.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeagueSeasonsAndResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Fixtures_SeasonId",
                table: "Fixtures");

            migrationBuilder.AddColumn<int>(
                name: "PointsForDraw",
                table: "Seasons",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PointsForWin",
                table: "Seasons",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "PointsPerRubber",
                table: "Seasons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RubberLengthKind",
                table: "Seasons",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "RaceTo");

            migrationBuilder.AddColumn<int>(
                name: "RubberLengthValue",
                table: "Seasons",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RubbersPerFixture",
                table: "Seasons",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Seasons",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Setup");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "Fixtures",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Fixtures",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Scheduled");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Fixtures",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_Status",
                table: "Seasons",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_SeasonId_Round",
                table: "Fixtures",
                columns: new[] { "SeasonId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_Status",
                table: "Fixtures",
                column: "Status");
        
            // Existing data: seasons that already have fixtures are under way, and fixtures that
            // already have rubbers have been played.
            migrationBuilder.Sql("""
                UPDATE "Seasons" SET "Status" = 'Scheduled'
                WHERE EXISTS (SELECT 1 FROM "Fixtures" f WHERE f."SeasonId" = "Seasons"."Id");
                UPDATE "Fixtures" SET "Status" = 'Completed'
                WHERE EXISTS (SELECT 1 FROM "Rubbers" r WHERE r."FixtureId" = "Fixtures"."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Seasons_Status",
                table: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Fixtures_SeasonId_Round",
                table: "Fixtures");

            migrationBuilder.DropIndex(
                name: "IX_Fixtures_Status",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "PointsForDraw",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "PointsForWin",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "PointsPerRubber",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "RubberLengthKind",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "RubberLengthValue",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "RubbersPerFixture",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Fixtures");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_SeasonId",
                table: "Fixtures",
                column: "SeasonId");
        }
    }
}
