using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGameEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameEntitlements",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GameId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GrantType = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameEntitlements", x => new { x.UserId, x.GameId });
                    table.ForeignKey(
                        name: "FK_GameEntitlements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameEntitlements_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GameEntitlements_GameId_GrantedAtUtc",
                table: "GameEntitlements",
                columns: new[] { "GameId", "GrantedAtUtc" });

            migrationBuilder.Sql(
                """
                INSERT INTO Games (Id, Name, Slug, Description, IconUrl, Status, CreatedAtUtc, UpdatedAtUtc)
                SELECT '33333333-3333-4333-8333-333333333333',
                       'Dash & Crash',
                       'dash-and-crash',
                       'Jogo de teste da plataforma.',
                       NULL,
                       'Published',
                       UTC_TIMESTAMP(6),
                       UTC_TIMESTAMP(6)
                FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM Games WHERE Slug = 'dash-and-crash');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameEntitlements");
        }
    }
}
