using Microsoft.EntityFrameworkCore;

namespace ClimaWatch.Infrastructure;

public sealed class ClimaWatchDbContext : DbContext
{
    public ClimaWatchDbContext(DbContextOptions<ClimaWatchDbContext> options)
        : base(options)
    {
    }

    public DbSet<WeatherCheck> WeatherChecks => Set<WeatherCheck>();

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
    }
}
