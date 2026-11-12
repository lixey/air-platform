namespace Platform.Api.Tests.Internals;

internal sealed class TrackingPlatformJob(ITenantContext tenantContext) : IPlatformJob
{
  public static TaskCompletionSource<(bool DefaultSystemScope, bool ExplicitSystemScope)> Completed { get; set; } =
    new(TaskCreationOptions.RunContinuationsAsynchronously);

  public Task ExecuteAsync(CancellationToken cancellationToken)
  {
    var defaultScope = tenantContext.IsSystemScope;
    bool explicitScope;
    using (AsyncLocalTenantContext.UseSystemScope())
    {
      explicitScope = tenantContext.IsSystemScope;
    }

    Completed.TrySetResult((defaultScope, explicitScope));
    return Task.CompletedTask;
  }
}
