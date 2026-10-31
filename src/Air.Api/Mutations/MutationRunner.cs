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
      var nestedResult = await action(ct);
      if (nestedResult is MutationResultDto { Outcome: not null and not MutationOutcome.Committed })
      {
        db.MarkMutationRollbackOnly();
      }

      return nestedResult;
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
        var wasRollbackOnly = db.IsMutationRollbackOnly;
        if (wasRollbackOnly || result is MutationResultDto { Outcome: not null and not MutationOutcome.Committed })
        {
          if (tx != null)
          {
            await tx.RollbackAsync(token);
          }

          db.ResetMutationState();
          if (wasRollbackOnly)
          {
            if (result is MutationResultDto { Outcome: null or MutationOutcome.Committed } dto)
            {
              dto.Outcome = MutationOutcome.Refused;
            }
            else if (result is MutationOutcome.Committed)
            {
              return (TResult)(object)MutationOutcome.Refused;
            }
            else if (result is not MutationResultDto and not string)
            {
              throw new InvalidOperationException("Nested mutation was marked rollback-only.");
            }
          }

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
    Func<TResult, long?>? extractId = null,
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
    if (IdempotencyStore.TryGetCommitted(tenantId, userId, serviceKey, trimmedKey, payloadHash, out var recordId))
    {
      if (recordId > 0 && reloadById != null)
      {
        var reloaded = await reloadById(recordId, ct);
        if (reloaded != null)
        {
          return reloaded;
        }
      }
    }

    var created = await RunAsync(db, executeCreate, ct);
    if (created is MutationResultDto { Outcome: not null and not MutationOutcome.Committed })
    {
      return created;
    }

    var createdId = extractId != null
      ? extractId(created)
      : (created as IEntity)?.Id;
    if (createdId is > 0)
    {
      IdempotencyStore.SaveCommitted(tenantId, userId, serviceKey, trimmedKey, payloadHash, createdId.Value);
    }

    return created;
  }
}
