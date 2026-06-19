using ClimaWatch.Api;
using ClimaWatch.Contracts;
using ClimaWatch.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Validar Connection String
var connectionString = builder.Configuration.GetConnectionString("ClimaWatch")
    ?? throw new InvalidOperationException("Connection string 'ClimaWatch' is not configured.");

// Registrar DbContext
builder.Services.AddDbContext<ClimaWatchDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHealthChecks();
builder.Services.AddSingleton<RabbitMqPublisher>();

var app = builder.Build();

// Aplicação automática de migrações no startup local
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var context = scope.ServiceProvider.GetRequiredService<ClimaWatchDbContext>();
    
    try
    {
        logger.LogInformation("Aplicando migrações do EF Core no startup da API...");
        await context.Database.MigrateAsync();
        logger.LogInformation("Migrações aplicadas com sucesso.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Falha crítica ao aplicar migrações do banco de dados.");
        throw;
    }
}

app.MapGet("/", () => Results.Ok(new
{
    service = "ClimaWatch.Api",
    status = "running"
}));

app.MapHealthChecks("/health");

app.MapPost("/api/weather-checks", async (
    WeatherCheckRequest request,
    ClimaWatchDbContext dbContext,
    RabbitMqPublisher publisher,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (request == null || string.IsNullOrWhiteSpace(request.City))
    {
        return Results.BadRequest(new { error = "A cidade é obrigatória." });
    }

    var city = request.City.Trim();
    if (city.Length > 120)
    {
        return Results.BadRequest(new { error = "O nome da cidade deve ter no máximo 120 caracteres." });
    }

    var weatherCheckId = Guid.NewGuid();
    var eventId = Guid.NewGuid();
    var correlationId = Guid.NewGuid();
    var requestedAtUtc = DateTimeOffset.UtcNow;

    var weatherCheck = new WeatherCheck
    {
        Id = weatherCheckId,
        EventId = eventId,
        CorrelationId = correlationId,
        City = city,
        Status = WeatherCheckStatuses.Queued,
        RequestedAtUtc = requestedAtUtc
    };

    try
    {
        dbContext.WeatherChecks.Add(weatherCheck);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao salvar a solicitação WeatherCheck no banco de dados.");
        return Results.Json(new { error = "Erro interno ao persistir solicitação." }, statusCode: 500);
    }

    var eventMessage = new WeatherCheckRequested(
        weatherCheckId,
        eventId,
        correlationId,
        city,
        requestedAtUtc);

    try
    {
        await publisher.PublishAsync(eventMessage, cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao publicar evento no RabbitMQ. Atualizando WeatherCheck para falho.");
        
        try
        {
            weatherCheck.Status = WeatherCheckStatuses.Failed;
            weatherCheck.ErrorMessage = "RabbitMQ publication failed.";
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception dbEx)
        {
            logger.LogError(dbEx, "Erro ao atualizar status do WeatherCheck para Failed no banco de dados.");
        }

        return Results.Json(new { error = "Serviço de mensageria temporariamente indisponível." }, statusCode: 503);
    }

    return Results.Accepted($"/api/weather-checks/{weatherCheckId}", new
    {
        status = WeatherCheckStatuses.Queued,
        weatherCheckId,
        eventId,
        correlationId,
        city
    });
});

app.MapGet("/api/weather-checks/{id:guid}", async (
    Guid id,
    ClimaWatchDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var weatherCheck = await dbContext.WeatherChecks
        .AsNoTracking()
        .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    if (weatherCheck == null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        weatherCheckId = weatherCheck.Id,
        eventId = weatherCheck.EventId,
        correlationId = weatherCheck.CorrelationId,
        city = weatherCheck.City,
        status = weatherCheck.Status,
        requestedAtUtc = weatherCheck.RequestedAtUtc,
        processedAtUtc = weatherCheck.ProcessedAtUtc,
        errorMessage = weatherCheck.ErrorMessage
    });
});

app.MapGet("/api/weather-checks/{id:guid}/snapshot", async (
    Guid id,
    ClimaWatchDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var snapshot = await dbContext.WeatherSnapshots
        .AsNoTracking()
        .FirstOrDefaultAsync(s => s.WeatherCheckId == id, cancellationToken);

    if (snapshot == null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        weatherSnapshotId = snapshot.Id,
        weatherCheckId = snapshot.WeatherCheckId,
        locationName = snapshot.LocationName,
        countryCode = snapshot.CountryCode,
        latitude = snapshot.Latitude,
        longitude = snapshot.Longitude,
        timezone = snapshot.Timezone,
        temperatureC = snapshot.TemperatureC,
        apparentTemperatureC = snapshot.ApparentTemperatureC,
        precipitationMm = snapshot.PrecipitationMm,
        windSpeedKmh = snapshot.WindSpeedKmh,
        weatherCode = snapshot.WeatherCode,
        observedAtUtc = snapshot.ObservedAtUtc
    });
});

app.MapGet("/api/weather-checks/{id:guid}/alerts", async (
    Guid id,
    ClimaWatchDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var alerts = await dbContext.WeatherAlerts
        .AsNoTracking()
        .Where(a => a.WeatherCheckId == id)
        .OrderBy(a => a.DetectedAtUtc)
        .Select(a => new
        {
            id = a.Id,
            alertType = a.AlertType,
            severity = a.Severity,
            message = a.Message,
            detectedAtUtc = a.DetectedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(alerts);
});

app.MapGet("/api/notifications", async (
    ClimaWatchDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var notifications = await dbContext.Notifications
        .AsNoTracking()
        .OrderByDescending(n => n.CreatedAtUtc)
        .Take(50)
        .Select(n => new
        {
            id = n.Id,
            weatherAlertId = n.WeatherAlertId,
            channel = n.Channel,
            status = n.Status,
            createdAtUtc = n.CreatedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(notifications);
});

app.Run();

public record WeatherCheckRequest(string? City);
