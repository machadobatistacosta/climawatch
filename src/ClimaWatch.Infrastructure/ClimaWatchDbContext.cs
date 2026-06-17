using Microsoft.EntityFrameworkCore;

namespace ClimaWatch.Infrastructure;

public sealed class ClimaWatchDbContext : DbContext
{
    public ClimaWatchDbContext(DbContextOptions<ClimaWatchDbContext> options)
        : base(options)
    {
    }

    public DbSet<WeatherCheck> WeatherChecks => Set<WeatherCheck>();
    public DbSet<WeatherSnapshot> WeatherSnapshots => Set<WeatherSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WeatherCheck>(entity =>
        {
            entity.ToTable("weather_checks");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.EventId)
                .HasColumnName("event_id")
                .IsRequired();

            entity.HasIndex(e => e.EventId)
                .IsUnique();

            entity.Property(e => e.CorrelationId)
                .HasColumnName("correlation_id")
                .IsRequired();

            entity.HasIndex(e => e.CorrelationId);

            entity.Property(e => e.City)
                .HasColumnName("city")
                .HasMaxLength(120)
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(e => e.RequestedAtUtc)
                .HasColumnName("requested_at_utc")
                .IsRequired();

            entity.Property(e => e.ProcessedAtUtc)
                .HasColumnName("processed_at_utc");

            entity.Property(e => e.ErrorMessage)
                .HasColumnName("error_message")
                .HasMaxLength(500);
        });

        modelBuilder.Entity<WeatherSnapshot>(entity =>
        {
            entity.ToTable("weather_snapshots");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.WeatherCheckId)
                .HasColumnName("weather_check_id")
                .IsRequired();

            entity.HasIndex(e => e.WeatherCheckId)
                .IsUnique();

            entity.Property(e => e.LocationName)
                .HasColumnName("location_name")
                .HasMaxLength(120)
                .IsRequired();

            entity.Property(e => e.CountryCode)
                .HasColumnName("country_code")
                .HasMaxLength(10)
                .IsRequired();

            entity.Property(e => e.Latitude)
                .HasColumnName("latitude")
                .IsRequired();

            entity.Property(e => e.Longitude)
                .HasColumnName("longitude")
                .IsRequired();

            entity.Property(e => e.Timezone)
                .HasColumnName("timezone")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.TemperatureC)
                .HasColumnName("temperature_c")
                .IsRequired();

            entity.Property(e => e.ApparentTemperatureC)
                .HasColumnName("apparent_temperature_c")
                .IsRequired();

            entity.Property(e => e.PrecipitationMm)
                .HasColumnName("precipitation_mm")
                .IsRequired();

            entity.Property(e => e.WindSpeedKmh)
                .HasColumnName("wind_speed_kmh")
                .IsRequired();

            entity.Property(e => e.WeatherCode)
                .HasColumnName("weather_code")
                .IsRequired();

            entity.Property(e => e.ObservedAtUtc)
                .HasColumnName("observed_at_utc")
                .IsRequired();

            entity.Property(e => e.RawPayloadJson)
                .HasColumnName("raw_payload_json")
                .HasColumnType("jsonb")
                .IsRequired();

            entity.HasOne(e => e.WeatherCheck)
                .WithOne()
                .HasForeignKey<WeatherSnapshot>(e => e.WeatherCheckId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
