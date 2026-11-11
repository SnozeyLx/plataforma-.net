using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCatalogAndProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    IconUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GameAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    GameId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    Points = table.Column<int>(type: "int", nullable: false),
                    TargetProgress = table.Column<long>(type: "bigint", nullable: false),
                    CriteriaJson = table.Column<string>(type: "json", nullable: true),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameAchievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameAchievements_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GamePlayerProgress",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GameId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Experience = table.Column<long>(type: "bigint", nullable: false),
                    MatchesPlayed = table.Column<long>(type: "bigint", nullable: false),
                    TotalPlaytimeSeconds = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastPlayedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePlayerProgress", x => new { x.UserId, x.GameId });
                    table.ForeignKey(
                        name: "FK_GamePlayerProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GamePlayerProgress_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserAchievementProgress",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AchievementId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProgressCurrent = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAchievementProgress", x => new { x.UserId, x.AchievementId });
                    table.ForeignKey(
                        name: "FK_UserAchievementProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAchievementProgress_GameAchievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "GameAchievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GameId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Score = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    ExperienceAwarded = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSessions_GamePlayerProgress_UserId_GameId",
                        columns: x => new { x.UserId, x.GameId },
                        principalTable: "GamePlayerProgress",
                        principalColumns: new[] { "UserId", "GameId" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GameAchievements_GameId_Code",
                table: "GameAchievements",
                columns: new[] { "GameId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameAchievements_GameId_Status",
                table: "GameAchievements",
                columns: new[] { "GameId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GamePlayerProgress_GameId_Experience",
                table: "GamePlayerProgress",
                columns: new[] { "GameId", "Experience" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_Slug",
                table: "Games",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_Status_Name",
                table: "Games",
                columns: new[] { "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_GameId_StartedAtUtc",
                table: "GameSessions",
                columns: new[] { "GameId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_UserId_GameId_StartedAtUtc",
                table: "GameSessions",
                columns: new[] { "UserId", "GameId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievementProgress_AchievementId_CompletedAtUtc",
                table: "UserAchievementProgress",
                columns: new[] { "AchievementId", "CompletedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSessions");

            migrationBuilder.DropTable(
                name: "UserAchievementProgress");

            migrationBuilder.DropTable(
                name: "GamePlayerProgress");

            migrationBuilder.DropTable(
                name: "GameAchievements");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
