using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimaWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeatherSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weather_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    weather_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    country_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    temperature_c = table.Column<double>(type: "double precision", nullable: false),
                    apparent_temperature_c = table.Column<double>(type: "double precision", nullable: false),
                    precipitation_mm = table.Column<double>(type: "double precision", nullable: false),
                    wind_speed_kmh = table.Column<double>(type: "double precision", nullable: false),
                    weather_code = table.Column<int>(type: "integer", nullable: false),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw_payload_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weather_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_weather_snapshots_weather_checks_weather_check_id",
                        column: x => x.weather_check_id,
                        principalTable: "weather_checks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_weather_snapshots_weather_check_id",
                table: "weather_snapshots",
                column: "weather_check_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "weather_snapshots");
        }
    }
}
