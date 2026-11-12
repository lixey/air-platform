namespace Platform.Api.Tests.Internals;

internal sealed class DisposableScopedGate
{
  public TaskCompletionSource EnteredUserLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public TaskCompletionSource ReleaseUserLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public TaskCompletionSource EnteredSessionLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public TaskCompletionSource ReleaseSessionLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
