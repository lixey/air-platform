namespace Platform.Api.Host.Internals;

internal sealed class PlatformJobHostedService(
  IServiceScopeFactory scopeFactory,
  ILogger<PlatformJobHostedService> logger,
  IEnumerable<PlatformJobRegistration> registrations) : BackgroundService
{
  protected override Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var tasks = registrations.Select(reg => RunJobLoopAsync(reg, stoppingToken)).ToArray();
    return tasks.Length == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
  }

  private async Task RunJobLoopAsync(PlatformJobRegistration registration, CancellationToken stoppingToken)
  {
    if (registration.InitialDelay is { TotalMilliseconds: > 0 } delay)
    {
      try
      {
        await Task.Delay(delay, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        return;
      }
    }

    using var timer = new PeriodicTimer(registration.Interval);
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService(registration.JobType) is IPlatformJob job)
        {
          await job.ExecuteAsync(stoppingToken);
        }
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Unhandled exception while executing platform job '{JobType}'.", registration.JobType.Name);
      }

      try
      {
        if (!await timer.WaitForNextTickAsync(stoppingToken))
        {
          break;
        }
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
    }
  }
}
