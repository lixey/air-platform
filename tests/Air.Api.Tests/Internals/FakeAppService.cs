namespace Platform.Api.Tests.Internals;

internal sealed class FakeAppService() : PlatformService()
{
  public Task<string> UnattributedHelper() => Task.FromResult("secret");

  [ServiceAction]
  public Task<string> ExplicitServiceAction() => Task.FromResult("ok");

  [RequireAction("Run")]
  public Task<string> GuardedAction() => Task.FromResult("ok");
}
