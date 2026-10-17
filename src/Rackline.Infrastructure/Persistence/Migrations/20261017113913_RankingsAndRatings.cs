using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rackline.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RankingsAndRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankingEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "RankingId",
                table: "Tournaments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Ratings",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Rankings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ParticipationPoints",
                table: "Rankings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int[]>(
                name: "PointsByPosition",
                table: "Rankings",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            // Existing rankings had no points table. Give them a name and a simple default table;
            // their old running totals had no tournaments behind them, so they cannot be carried over.
            migrationBuilder.Sql("""
                UPDATE "Rankings"
                SET "Name" = "Season" || ' rankings',
                    "PointsByPosition" = '{100,75,55,40,25,15}',
                    "ParticipationPoints" = 5
                WHERE cardinality("PointsByPosition") = 0;
                """);

            migrationBuilder.CreateTable(
                name: "RankingAwards",
                columns: table => new
                {
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingAwards", x => new { x.RankingId, x.TournamentId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_RankingAwards_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankingAwards_Rankings_RankingId",
                        column: x => x.RankingId,
                        principalTable: "Rankings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankingAwards_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RatingChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Discipline = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Won = table.Column<bool>(type: "boolean", nullable: false),
                    Before = table.Column<double>(type: "double precision", nullable: false),
                    After = table.Column<double>(type: "double precision", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatingChanges_Players_OpponentId",
                        column: x => x.OpponentId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RatingChanges_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_RankingId",
                table: "Tournaments",
                column: "RankingId");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_Discipline_Value",
                table: "Ratings",
                columns: new[] { "Discipline", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_RankingAwards_PlayerId",
                table: "RankingAwards",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RankingAwards_TournamentId",
                table: "RankingAwards",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_RatingChanges_MatchId",
                table: "RatingChanges",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RatingChanges_OpponentId",
                table: "RatingChanges",
                column: "OpponentId");

            migrationBuilder.CreateIndex(
                name: "IX_RatingChanges_PlayerId_Discipline_At",
                table: "RatingChanges",
                columns: new[] { "PlayerId", "Discipline", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_RatingChanges_PlayerId_MatchId",
                table: "RatingChanges",
                columns: new[] { "PlayerId", "MatchId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tournaments_Rankings_RankingId",
                table: "Tournaments",
                column: "RankingId",
                principalTable: "Rankings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tournaments_Rankings_RankingId",
                table: "Tournaments");

            migrationBuilder.DropTable(
                name: "RankingAwards");

            migrationBuilder.DropTable(
                name: "RatingChanges");

            migrationBuilder.DropIndex(
                name: "IX_Tournaments_RankingId",
                table: "Tournaments");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_Discipline_Value",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "RankingId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Rankings");

            migrationBuilder.DropColumn(
                name: "ParticipationPoints",
                table: "Rankings");

            migrationBuilder.DropColumn(
                name: "PointsByPosition",
                table: "Rankings");

            migrationBuilder.CreateTable(
                name: "RankingEntries",
                columns: table => new
                {
                    RankingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingEntries", x => new { x.RankingId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_RankingEntries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankingEntries_Rankings_RankingId",
                        column: x => x.RankingId,
                        principalTable: "Rankings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankingEntries_PlayerId",
                table: "RankingEntries",
                column: "PlayerId");
        }
    }
}
