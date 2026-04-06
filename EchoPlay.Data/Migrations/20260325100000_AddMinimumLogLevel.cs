using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMinimumLogLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinimumLogLevel",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                // 2 = LogLevel.Information – neue DBs starten direkt mit dem sinnvollen Standard.
                // Bestehende Zeilen erhalten denselben Wert, weil EF den defaultValue als ALTER TABLE DEFAULT einsetzt.
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinimumLogLevel",
                table: "AppSettings");
        }
    }
}
