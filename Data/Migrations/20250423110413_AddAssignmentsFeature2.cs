using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace webcodework_be.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentsFeature2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_submittedFiles_AssignmentSubmissions_AssignmentSubmissionId",
                table: "submittedFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_submittedFiles",
                table: "submittedFiles");

            migrationBuilder.RenameTable(
                name: "submittedFiles",
                newName: "SubmittedFiles");

            migrationBuilder.RenameIndex(
                name: "IX_submittedFiles_AssignmentSubmissionId",
                table: "SubmittedFiles",
                newName: "IX_SubmittedFiles_AssignmentSubmissionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubmittedFiles",
                table: "SubmittedFiles",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SubmittedFiles_AssignmentSubmissions_AssignmentSubmissionId",
                table: "SubmittedFiles",
                column: "AssignmentSubmissionId",
                principalTable: "AssignmentSubmissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubmittedFiles_AssignmentSubmissions_AssignmentSubmissionId",
                table: "SubmittedFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubmittedFiles",
                table: "SubmittedFiles");

            migrationBuilder.RenameTable(
                name: "SubmittedFiles",
                newName: "submittedFiles");

            migrationBuilder.RenameIndex(
                name: "IX_SubmittedFiles_AssignmentSubmissionId",
                table: "submittedFiles",
                newName: "IX_submittedFiles_AssignmentSubmissionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_submittedFiles",
                table: "submittedFiles",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_submittedFiles_AssignmentSubmissions_AssignmentSubmissionId",
                table: "submittedFiles",
                column: "AssignmentSubmissionId",
                principalTable: "AssignmentSubmissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
