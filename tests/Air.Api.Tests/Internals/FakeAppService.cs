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

  [ServiceAction]
  public Task<string> CalculateSummaryAsync(StrictNumberPayload payload) =>
    Task.FromResult(FormattableString.Invariant($"sum:{payload.Count}:{payload.Amount}"));

  [ServiceAction]
  public Task<string> ThrowQueryErrorAsync() =>
    throw new QueryRequestException(QueryError.UnknownField, "'secretField' is not a field of this list.", "secretField");

  [ServiceAction]
  public Task<string> ThrowIdempotencyConflictAsync() =>
    throw new IdempotencyConflictException("Idempotency-Key has already been used for a different request.");

  [ServiceAction]
  public Task<string> ThrowConcurrencyConflictAsync() =>
    throw new DbUpdateConcurrencyException("Row version mismatch.");
}
