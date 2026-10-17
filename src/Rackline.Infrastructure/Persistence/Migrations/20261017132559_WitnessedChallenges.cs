using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rackline.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WitnessedChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Challenges used to be proposals between players with no organisation. Nothing could record
            // one before this change, so the only rows are invented sample proposals with no result.
            // They cannot be given an organisation, so they go.
            migrationBuilder.Sql("""DELETE FROM "Challenges";""");

            migrationBuilder.DropForeignKey(
                name: "FK_Challenges_Players_ChallengerId",
                table: "Challenges");

            migrationBuilder.DropForeignKey(
                name: "FK_Challenges_Players_OpponentId",
                table: "Challenges");

            migrationBuilder.DropIndex(
                name: "IX_Challenges_ChallengerId",
                table: "Challenges");

            migrationBuilder.DropIndex(
                name: "IX_Challenges_Status",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Challenges");

            migrationBuilder.RenameColumn(
                name: "OpponentScore",
                table: "Challenges",
                newName: "Score2");

            migrationBuilder.RenameColumn(
                name: "OpponentId",
                table: "Challenges",
                newName: "Player2Id");

            migrationBuilder.RenameColumn(
                name: "ChallengerScore",
                table: "Challenges",
                newName: "Score1");

            migrationBuilder.RenameColumn(
                name: "ChallengerId",
                table: "Challenges",
                newName: "Player1Id");

            migrationBuilder.RenameIndex(
                name: "IX_Challenges_OpponentId",
                table: "Challenges",
                newName: "IX_Challenges_Player2Id");

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Challenges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlayedAt",
                table: "Challenges",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<bool>(
                name: "Rated",
                table: "Challenges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UnratedReason",
                table: "Challenges",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_OrganisationId",
                table: "Challenges",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_PlayedAt",
                table: "Challenges",
                column: "PlayedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_Player1Id_Player2Id_Discipline",
                table: "Challenges",
                columns: new[] { "Player1Id", "Player2Id", "Discipline" });

            migrationBuilder.AddForeignKey(
                name: "FK_Challenges_Organisations_OrganisationId",
                table: "Challenges",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Challenges_Players_Player1Id",
                table: "Challenges",
                column: "Player1Id",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Challenges_Players_Player2Id",
                table: "Challenges",
                column: "Player2Id",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Challenges_Organisations_OrganisationId",
                table: "Challenges");

            migrationBuilder.DropForeignKey(
                name: "FK_Challenges_Players_Player1Id",
                table: "Challenges");

            migrationBuilder.DropForeignKey(
                name: "FK_Challenges_Players_Player2Id",
                table: "Challenges");

            migrationBuilder.DropIndex(
                name: "IX_Challenges_OrganisationId",
                table: "Challenges");

            migrationBuilder.DropIndex(
                name: "IX_Challenges_PlayedAt",
                table: "Challenges");

            migrationBuilder.DropIndex(
                name: "IX_Challenges_Player1Id_Player2Id_Discipline",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "PlayedAt",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "Rated",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "UnratedReason",
                table: "Challenges");

            migrationBuilder.RenameColumn(
                name: "Score2",
                table: "Challenges",
                newName: "OpponentScore");

            migrationBuilder.RenameColumn(
                name: "Score1",
                table: "Challenges",
                newName: "ChallengerScore");

            migrationBuilder.RenameColumn(
                name: "Player2Id",
                table: "Challenges",
                newName: "OpponentId");

            migrationBuilder.RenameColumn(
                name: "Player1Id",
                table: "Challenges",
                newName: "ChallengerId");

            migrationBuilder.RenameIndex(
                name: "IX_Challenges_Player2Id",
                table: "Challenges",
                newName: "IX_Challenges_OpponentId");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "Challenges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Challenges",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_ChallengerId",
                table: "Challenges",
                column: "ChallengerId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_Status",
                table: "Challenges",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Challenges_Players_ChallengerId",
                table: "Challenges",
                column: "ChallengerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Challenges_Players_OpponentId",
                table: "Challenges",
                column: "OpponentId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
