using ClimaWatch.WeatherWorker;
using ClimaWatch.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Validar Connection String
var connectionString = builder.Configuration.GetConnectionString("ClimaWatch")
    ?? throw new InvalidOperationException("Connection string 'ClimaWatch' is not configured.");

// Registrar DbContext
builder.Services.AddDbContext<ClimaWatchDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHostedService<QueueConsumerWorker>();

var host = builder.Build();
host.Run();
