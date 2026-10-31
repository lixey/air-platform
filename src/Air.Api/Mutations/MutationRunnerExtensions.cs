namespace Platform.Api.Mutations;

public static class MutationRunnerExtensions
{
  public static Task<TResult> RunMutationAsync<TResult>(
    this PlatformDbContext db,
    Func<CancellationToken, Task<TResult>> action,
    CancellationToken ct = default) =>
    MutationRunner.RunAsync(db, action, ct);

  public static Task RunMutationAsync(
    this PlatformDbContext db,
    Func<CancellationToken, Task> action,
    CancellationToken ct = default) =>
    MutationRunner.RunAsync(db, action, ct);
}
