using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceReplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Abgleich über einen gemeinsamen Ordner: zwei gerätegebundene Einstellungen (Ordner, an
            // oder aus; aus ist der Vorgabewert und für den Bestand richtig) und eine Tabelle mit dem
            // Lesestand je Gerät. Reine Erweiterung, keine Daten werden angefasst.
            _ = migrationBuilder.AddColumn<bool>(
                name: "ReplicationEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            _ = migrationBuilder.AddColumn<string>(
                name: "ReplicationFolderPath",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            _ = migrationBuilder.CreateTable(
                name: "ReplicationPeers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    ExportedStamp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AppliedRecords = table.Column<long>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ChangeStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_ReplicationPeers", x => x.Id);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_ReplicationPeers_DeviceId",
                table: "ReplicationPeers",
                column: "DeviceId",
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "ReplicationPeers");

            _ = migrationBuilder.DropColumn(
                name: "ReplicationEnabled",
                table: "AppSettings");

            _ = migrationBuilder.DropColumn(
                name: "ReplicationFolderPath",
                table: "AppSettings");
        }
    }
}
