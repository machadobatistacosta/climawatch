using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimaWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWeatherChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weather_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weather_checks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_weather_checks_correlation_id",
                table: "weather_checks",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_weather_checks_event_id",
                table: "weather_checks",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "weather_checks");
        }
    }
}
