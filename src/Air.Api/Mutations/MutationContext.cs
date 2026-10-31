using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Platform.Api.Mutations;

public class MutationContext(PlatformDbContext? db = null) : IMutationContext
{
  private readonly List<MutationError> _errors = [];
  private readonly List<MutationError> _warnings = [];

  public IReadOnlyList<MutationError> Errors => _errors;
  public IReadOnlyList<MutationError> Warnings => _warnings;

  public void AddError(MutationError error)
  {
    _errors.Add(error);
  }

  public void AddError(string code, string message, long? recordId = null, string? path = null)
  {
    _errors.Add(new MutationError(code, message, path, recordId));
  }

  public void Warn(MutationError warning)
  {
    _warnings.Add(warning);
  }

  public void Warn(string message, string? code = null, long? recordId = null, string? path = null)
  {
    _warnings.Add(new MutationError(code ?? "mutation.warning", message, path, recordId));
  }

  public virtual async Task FlushAsync(CancellationToken cancellationToken)
  {
    if (db != null)
    {
      await db.SaveChangesAsync(cancellationToken);
    }
  }

  public virtual Task LockAsync<TLocked>(CancellationToken cancellationToken, params long[] key) where TLocked : class =>
    AcquireLocksAsync(db, typeof(TLocked), [key], cancellationToken);

  public virtual Task LockAsync<TLocked>(IEnumerable<long[]> keys, CancellationToken cancellationToken) where TLocked : class =>
    AcquireLocksAsync(db, typeof(TLocked), keys, cancellationToken);

  internal static long LockKeyOf(Type lockedType, IReadOnlyList<long> key)
  {
    var numbers = string.Join(':', key.Select(n => n.ToString(CultureInfo.InvariantCulture)));
    var prefix = $"air-lock:{lockedType.Name}:{numbers}";
    return BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(prefix)));
  }

  internal static async Task AcquireLocksAsync(PlatformDbContext? db, Type lockedType, IEnumerable<long[]> keys, CancellationToken ct)
  {
    if (db == null || db.Database.CurrentTransaction == null)
    {
      throw new InvalidOperationException("Cannot acquire a mutation lock outside an active database transaction.");
    }

    var keyArrays = keys as IReadOnlyList<long[]> ?? keys.ToList();
    var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    var isSqlServer = db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
    if (isPostgres || isSqlServer)
    {
      var lockKeys = keyArrays.Select(k => LockKeyOf(lockedType, k)).Distinct().OrderBy(k => k).ToList();
      foreach (var lockKey in lockKeys)
      {
        if (isPostgres)
        {
          await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        }
        else if (isSqlServer)
        {
          var resource = $"air-lock:{lockKey}";
          await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @res int; EXEC @res = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @res < 0 THROW 51000, 'Failed to acquire application lock.', 1;",
            ct);
        }
      }
    }

    var singleIds = keyArrays.Where(k => k.Length == 1).Select(k => k[0]).ToHashSet();
    if (singleIds.Count > 0)
    {
      foreach (var entry in db.ChangeTracker.Entries()
        .Where(e => e.State == EntityState.Unchanged && lockedType.IsAssignableFrom(e.Metadata.ClrType) && e.Entity is IEntity ent && singleIds.Contains(ent.Id))
        .ToList())
      {
        await entry.ReloadAsync(ct);
      }
    }
  }
}
