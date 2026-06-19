using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ClimaWatch.Infrastructure;
using ClimaWatch.NotificationWorker;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ClimaWatch")
    ?? throw new InvalidOperationException("ConnectionString 'ClimaWatch' not configured.");

builder.Services.AddDbContext<ClimaWatchDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHostedService<NotificationConsumerWorker>();

var host = builder.Build();
host.Run();
