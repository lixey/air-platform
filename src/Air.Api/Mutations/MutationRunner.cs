using Platform.Api.Mutations.Internals;

namespace Platform.Api.Mutations;

public static class MutationRunner
{
  public static async Task<TResult> RunAsync<TResult>(
    PlatformDbContext db,
    Func<CancellationToken, Task<TResult>> action,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(action);

    if (db.IsInMutationRun)
    {
      using var nested = db.BeginMutationRun();
      return await action(ct);
    }

    return await MutationExecutionStrategy.RunAsync(db, async token =>
    {
      using var run = db.BeginMutationRun();
      await using var tx = db.Database.CurrentTransaction == null && db.Database.IsRelational()
        ? await db.Database.BeginTransactionAsync(token)
        : null;

      try
      {
        var result = await action(token);
        if (result is MutationResultDto { Outcome: not null and not MutationOutcome.Committed })
        {
          if (tx != null)
          {
            await tx.RollbackAsync(token);
          }

          db.ResetMutationState();
          return result;
        }

        await db.FlushMutationJournalAsync(token);
        if (tx != null)
        {
          await tx.CommitAsync(token);
        }

        return result;
      }
      catch
      {
        if (tx != null)
        {
          try
          {
            await tx.RollbackAsync(CancellationToken.None);
          }
          catch
          {
          }
        }

        db.ResetMutationState();
        throw;
      }
    }, ct);
  }

  public static async Task RunAsync(
    PlatformDbContext db,
    Func<CancellationToken, Task> action,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(action);

    await RunAsync(db, async token =>
    {
      await action(token);
      return true;
    }, ct);
  }

  public static async Task<TResult> RunIdempotentCreateAsync<TResult>(
    PlatformDbContext db,
    string serviceKey,
    string? idempotencyKey,
    string rawPayload,
    Func<CancellationToken, Task<TResult>> executeCreate,
    Func<long, CancellationToken, Task<TResult?>>? reloadById = null,
    CancellationToken ct = default)
    where TResult : class
  {
    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
      return await RunAsync(db, executeCreate, ct);
    }

    var trimmedKey = idempotencyKey.Trim();
    var tenantId = db.TenantContext.TenantId;
    var userId = db.TenantContext.UserId;
    var payloadHash = IdempotencyStore.ComputePayloadHash(rawPayload);

    using var keyLock = await IdempotencyStore.AcquireLockAsync(tenantId, userId, trimmedKey, ct);
    if (IdempotencyStore.TryGetCommitted(tenantId, userId, serviceKey, trimmedKey, payloadHash, out var recordId, out var resultJson))
    {
      if (recordId is > 0 && reloadById != null)
      {
        var reloaded = await reloadById(recordId.Value, ct);
        if (reloaded != null)
        {
          return reloaded;
        }
      }

      if (!string.IsNullOrEmpty(resultJson))
      {
        var deserialized = JsonSerializer.Deserialize<TResult>(resultJson, PlatformJsonOptions.DefaultOptions);
        if (deserialized != null)
        {
          return deserialized;
        }
      }
    }

    var created = await RunAsync(db, executeCreate, ct);
    var createdId = (created as IEntity)?.Id;
    var serialized = created is IEntity ? null : JsonSerializer.Serialize(created, PlatformJsonOptions.DefaultOptions);
    IdempotencyStore.SaveCommitted(tenantId, userId, serviceKey, trimmedKey, payloadHash, createdId is > 0 ? createdId : null, serialized);
    return created;
  }
}
