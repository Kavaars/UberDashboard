using Microsoft.Extensions.Hosting;
using UberDashboard.Application.Synchronization;

namespace UberDashboard.App.Hosting;

internal sealed class IntegrationSyncWorker(SyncScheduler scheduler) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => scheduler.RunAsync(stoppingToken);
}
