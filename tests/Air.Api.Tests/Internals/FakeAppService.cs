using Microsoft.AspNetCore.Http;

namespace Platform.Api.Tests.Internals;

internal sealed class FakeAppService() : PlatformService()
{
  public override Feature Feature { get; } = new("Fake", "App", typeof(FakeAppService));

  public Task<string> UnattributedHelper() => Task.FromResult("secret");

  [ServiceAction]
  public Task<string> ExplicitServiceAction() => Task.FromResult("ok");

  [RequireAction("Run")]
  public Task<string> GuardedAction() => Task.FromResult("ok");

  [RequireAction("GhostRight")]
  public Task<string> UndeclaredAction() => Task.FromResult("nope");

  [ServiceAction]
  public Task<IResult> EchoPositional(long id, PositionalCommandPayload payload) =>
    Task.FromResult(Microsoft.AspNetCore.Http.Results.Ok(new { text = $"{id}:{payload.Title}:{payload.Priority}" }));
}
