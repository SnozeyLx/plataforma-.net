using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedPlayerName",
                table: "AspNetUsers",
                type: "varchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerName",
                table: "AspNetUsers",
                type: "varchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FriendConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    LowerUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UpperUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FriendConnections_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FriendConnections_AspNetUsers_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserBlocks",
                columns: table => new
                {
                    BlockerUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    BlockedUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlocks", x => new { x.BlockerUserId, x.BlockedUserId });
                    table.ForeignKey(
                        name: "FK_UserBlocks_AspNetUsers_BlockedUserId",
                        column: x => x.BlockedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBlocks_AspNetUsers_BlockerUserId",
                        column: x => x.BlockerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_NormalizedPlayerName",
                table: "AspNetUsers",
                column: "NormalizedPlayerName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FriendConnections_LowerUserId_UpperUserId",
                table: "FriendConnections",
                columns: new[] { "LowerUserId", "UpperUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FriendConnections_RecipientUserId_Status_CreatedAtUtc",
                table: "FriendConnections",
                columns: new[] { "RecipientUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FriendConnections_RequesterUserId_Status_CreatedAtUtc",
                table: "FriendConnections",
                columns: new[] { "RequesterUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedUserId",
                table: "UserBlocks",
                column: "BlockedUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FriendConnections");

            migrationBuilder.DropTable(
                name: "UserBlocks");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_NormalizedPlayerName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedPlayerName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PlayerName",
                table: "AspNetUsers");
        }
    }
}
