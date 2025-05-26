using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace webcodework_be.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfilePhotoFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoContentType",
                table: "Users",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoOriginalName",
                table: "Users",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoPath",
                table: "Users",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoStoredName",
                table: "Users",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePhotoContentType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoOriginalName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoPath",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoStoredName",
                table: "Users");
        }
    }
}
