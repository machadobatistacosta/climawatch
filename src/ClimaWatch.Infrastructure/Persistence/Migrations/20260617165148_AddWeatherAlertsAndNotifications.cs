using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimaWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeatherAlertsAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weather_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weather_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weather_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    detected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weather_alerts", x => x.id);
                    table.ForeignKey(
                        name: "FK_weather_alerts_weather_checks_weather_check_id",
                        column: x => x.weather_check_id,
                        principalTable: "weather_checks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_weather_alerts_weather_snapshots_weather_snapshot_id",
                        column: x => x.weather_snapshot_id,
                        principalTable: "weather_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    weather_alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_notifications_weather_alerts_weather_alert_id",
                        column: x => x.weather_alert_id,
                        principalTable: "weather_alerts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_weather_alert_id_channel",
                table: "notifications",
                columns: new[] { "weather_alert_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_weather_alerts_event_id",
                table: "weather_alerts",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_weather_alerts_weather_check_id",
                table: "weather_alerts",
                column: "weather_check_id");

            migrationBuilder.CreateIndex(
                name: "IX_weather_alerts_weather_snapshot_id",
                table: "weather_alerts",
                column: "weather_snapshot_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "weather_alerts");
        }
    }
}
