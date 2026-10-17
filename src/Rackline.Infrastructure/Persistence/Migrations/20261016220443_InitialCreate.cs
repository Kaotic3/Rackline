using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rackline.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Organisations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Organisations_Organisations_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Nationality = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    LicenceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OrganisationIds = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Discipline = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leagues_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Rankings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Discipline = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Season = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rankings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rankings_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Discipline = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchLengthKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchLengthValue = table.Column<int>(type: "integer", nullable: false),
                    GrandFinalReset = table.Column<bool>(type: "boolean", nullable: false),
                    GroupCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tournaments_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Venues_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Discipline = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChallengerScore = table.Column<int>(type: "integer", nullable: false),
                    OpponentScore = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MatchLengthKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchLengthValue = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Challenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Challenges_Players_ChallengerId",
                        column: x => x.ChallengerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Challenges_Players_OpponentId",
                        column: x => x.OpponentId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ratings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Discipline = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    MatchesPlayed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ratings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ratings_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RankingEntries",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankingId = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "Entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: true),
                    Handicap = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Entries_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tables_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HomeVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlayerIds = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Teams_Venues_HomeVenueId",
                        column: x => x.HomeVenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Side = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Group = table.Column<int>(type: "integer", nullable: true),
                    FramesToWin = table.Column<int>(type: "integer", nullable: false),
                    Score1 = table.Column<int>(type: "integer", nullable: false),
                    Score2 = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WinnerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    LoserEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    TableId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResetMatchNumber = table.Column<int>(type: "integer", nullable: true),
                    LoserToMatchNumber = table.Column<int>(type: "integer", nullable: true),
                    LoserToSlot = table.Column<int>(type: "integer", nullable: true),
                    Slot1EntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Slot1Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Slot2EntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Slot2Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WinnerToMatchNumber = table.Column<int>(type: "integer", nullable: true),
                    WinnerToSlot = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Tables_TableId",
                        column: x => x.TableId,
                        principalTable: "Tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Matches_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Fixtures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fixtures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fixtures_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Fixtures_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fixtures_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Frames",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WinnerSlot = table.Column<int>(type: "integer", nullable: false),
                    BreakAndRun = table.Column<bool>(type: "boolean", nullable: false),
                    Points1 = table.Column<int>(type: "integer", nullable: true),
                    Points2 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Frames", x => new { x.MatchId, x.Number });
                    table.ForeignKey(
                        name: "FK_Frames_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rubbers",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomePlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeScore = table.Column<int>(type: "integer", nullable: false),
                    AwayScore = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rubbers", x => new { x.FixtureId, x.Number });
                    table.ForeignKey(
                        name: "FK_Rubbers_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Rubbers_Players_AwayPlayerId",
                        column: x => x.AwayPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rubbers_Players_HomePlayerId",
                        column: x => x.HomePlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_ChallengerId",
                table: "Challenges",
                column: "ChallengerId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_OpponentId",
                table: "Challenges",
                column: "OpponentId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_Status",
                table: "Challenges",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_PlayerId",
                table: "Entries",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_TournamentId_PlayerId",
                table: "Entries",
                columns: new[] { "TournamentId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_AwayTeamId",
                table: "Fixtures",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_HomeTeamId",
                table: "Fixtures",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_ScheduledFor",
                table: "Fixtures",
                column: "ScheduledFor");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_SeasonId",
                table: "Fixtures",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_OrganisationId",
                table: "Leagues",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_LoserEntryId",
                table: "Matches",
                column: "LoserEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TableId",
                table: "Matches",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TournamentId_Number",
                table: "Matches",
                columns: new[] { "TournamentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TournamentId_State",
                table: "Matches",
                columns: new[] { "TournamentId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_WinnerEntryId",
                table: "Matches",
                column: "WinnerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_Level",
                table: "Organisations",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_ParentId",
                table: "Organisations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_LastName_FirstName",
                table: "Players",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_LicenceNumber",
                table: "Players",
                column: "LicenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Players_OrganisationIds",
                table: "Players",
                column: "OrganisationIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_RankingEntries_PlayerId",
                table: "RankingEntries",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Rankings_OrganisationId_Discipline_Season",
                table: "Rankings",
                columns: new[] { "OrganisationId", "Discipline", "Season" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_PlayerId_Discipline",
                table: "Ratings",
                columns: new[] { "PlayerId", "Discipline" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rubbers_AwayPlayerId",
                table: "Rubbers",
                column: "AwayPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Rubbers_HomePlayerId",
                table: "Rubbers",
                column: "HomePlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_LeagueId",
                table: "Seasons",
                column: "LeagueId");

            migrationBuilder.CreateIndex(
                name: "IX_Tables_VenueId_Label",
                table: "Tables",
                columns: new[] { "VenueId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_HomeVenueId",
                table: "Teams",
                column: "HomeVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_PlayerIds",
                table: "Teams",
                column: "PlayerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_SeasonId_Name",
                table: "Teams",
                columns: new[] { "SeasonId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_OrganisationId",
                table: "Tournaments",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_Status",
                table: "Tournaments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Venues_OrganisationId",
                table: "Venues",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Challenges");

            migrationBuilder.DropTable(
                name: "Entries");

            migrationBuilder.DropTable(
                name: "Frames");

            migrationBuilder.DropTable(
                name: "RankingEntries");

            migrationBuilder.DropTable(
                name: "Ratings");

            migrationBuilder.DropTable(
                name: "Rubbers");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Rankings");

            migrationBuilder.DropTable(
                name: "Fixtures");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Tables");

            migrationBuilder.DropTable(
                name: "Tournaments");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "Venues");

            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropTable(
                name: "Organisations");
        }
    }
}
