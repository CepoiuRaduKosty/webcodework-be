using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace webcodework_be.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationSummaryToSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastEvaluatedAt",
                table: "AssignmentSubmissions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEvaluationDetailsJson",
                table: "AssignmentSubmissions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LastEvaluationOverallStatus",
                table: "AssignmentSubmissions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "LastEvaluationPointsObtained",
                table: "AssignmentSubmissions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastEvaluationTotalPossiblePoints",
                table: "AssignmentSubmissions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastEvaluatedAt",
                table: "AssignmentSubmissions");

            migrationBuilder.DropColumn(
                name: "LastEvaluationDetailsJson",
                table: "AssignmentSubmissions");

            migrationBuilder.DropColumn(
                name: "LastEvaluationOverallStatus",
                table: "AssignmentSubmissions");

            migrationBuilder.DropColumn(
                name: "LastEvaluationPointsObtained",
                table: "AssignmentSubmissions");

            migrationBuilder.DropColumn(
                name: "LastEvaluationTotalPossiblePoints",
                table: "AssignmentSubmissions");
        }
    }
}
