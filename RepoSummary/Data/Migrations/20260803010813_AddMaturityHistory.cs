using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepoSummary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaturityHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaturityHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepoFullName = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Grade = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaturityHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaturityHistory_RepoFullName",
                table: "MaturityHistory",
                column: "RepoFullName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaturityHistory");
        }
    }
}
