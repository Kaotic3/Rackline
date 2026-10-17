using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rackline.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganisationStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Appointments_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganisationId_UserId",
                table: "Appointments",
                columns: new[] { "OrganisationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_UserId",
                table: "Appointments",
                column: "UserId");

            // The site-wide Organiser and Referee roles used to cover every organisation. Keep what
            // their holders could do by appointing them at every top-level organisation, which reaches
            // everything below. Someone who held both becomes an organiser. Then retire the roles;
            // deleting a role also removes it from its users.
            migrationBuilder.Sql("""
                INSERT INTO "Appointments" ("Id", "OrganisationId", "UserId", "Role")
                SELECT gen_random_uuid(), o."Id", ur."UserId",
                       CASE WHEN bool_or(ur."RoleId" = '0193a000-0000-7000-8000-000000000002') THEN 'Organiser' ELSE 'Referee' END
                FROM "AspNetUserRoles" ur
                CROSS JOIN "Organisations" o
                WHERE ur."RoleId" IN ('0193a000-0000-7000-8000-000000000002', '0193a000-0000-7000-8000-000000000003')
                  AND o."ParentId" IS NULL
                GROUP BY o."Id", ur."UserId";
                """);

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("0193a000-0000-7000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("0193a000-0000-7000-8000-000000000003"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("0193a000-0000-7000-8000-000000000002"), "0193a000-0000-7000-8000-000000000002", "Organiser", "ORGANISER" },
                    { new Guid("0193a000-0000-7000-8000-000000000003"), "0193a000-0000-7000-8000-000000000003", "Referee", "REFEREE" }
                });
        }
    }
}
