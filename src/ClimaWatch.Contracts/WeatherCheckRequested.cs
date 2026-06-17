using System;

namespace ClimaWatch.Contracts;

public sealed record WeatherCheckRequested(
    Guid WeatherCheckId,
    Guid EventId,
    Guid CorrelationId,
    string City,
    DateTimeOffset RequestedAtUtc);
