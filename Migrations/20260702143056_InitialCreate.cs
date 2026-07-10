using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaskFlow.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TodoTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Titre = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Priorite = table.Column<int>(type: "INTEGER", nullable: false),
                    EstTerminee = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateEcheance = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoTasks", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "TodoTasks",
                columns: new[] { "Id", "DateEcheance", "Description", "EstTerminee", "Priorite", "Titre" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 8, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), null, false, 0, "Préparer le support de cours" },
                    { 2, new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), null, false, 2, "Installer le ventillateur du plafond" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TodoTasks");
        }
    }
}
