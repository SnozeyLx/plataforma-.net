using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProfileIconId",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileIconId",
                table: "AspNetUsers");
        }
    }
}
