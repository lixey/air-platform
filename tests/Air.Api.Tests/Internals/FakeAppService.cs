using Microsoft.AspNetCore.Http;

namespace Platform.Api.Tests.Internals;

internal sealed class FakeAppService() : PlatformService()
{
  public override Feature Feature { get; } = new("Fake", "App", typeof(FakeAppService));

  public Task<string> UnattributedHelper() => Task.FromResult("secret");

  [Endpoint]
  public Task<string> ExplicitServiceAction() => Task.FromResult("ok");

  [RequireAction("Run")]
  public Task<string> GuardedAction() => Task.FromResult("ok");

  [RequireAction("GhostRight")]
  public Task<string> UndeclaredAction() => Task.FromResult("nope");

  [Endpoint]
  public Task<IResult> EchoPositional(long id, PositionalCommandPayload payload) =>
    Task.FromResult(Microsoft.AspNetCore.Http.Results.Ok(new { text = $"{id}:{payload.Title}:{payload.Priority}" }));

  [Endpoint]
  public Task<string> CalculateSummaryAsync(StrictNumberPayload payload) =>
    Task.FromResult(FormattableString.Invariant($"sum:{payload.Count}:{payload.Amount}"));

  [Endpoint]
  public Task<string> ThrowQueryErrorAsync() =>
    throw new QueryRequestException(QueryError.UnknownField, "'secretField' is not a field of this list.", "secretField");

  [Endpoint]
  public Task<string> ThrowIdempotencyConflictAsync() =>
    throw new IdempotencyConflictException("Idempotency-Key has already been used for a different request.");

  [Endpoint]
  public Task<string> ThrowConcurrencyConflictAsync() =>
    throw new DbUpdateConcurrencyException("Row version mismatch.");

  [Endpoint]
  public Task<IResult> EchoCustomScoped(CustomScopedPayload payload) =>
    Task.FromResult(Results.Ok(new
    {
      id = payload.Id,
      parentId = payload.ParentId,
      hasRawPayload = payload.RawPayload is not null
    }));
}
