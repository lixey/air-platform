using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Platform.Api.Mutations;

public class MutationContext(PlatformDbContext? db = null) : IMutationContext
{
  private readonly List<MutationError> _errors = [];

  public IReadOnlyList<MutationError> Errors => _errors;

  public void AddError(MutationError error)
  {
    _errors.Add(error);
  }

  public void AddError(string code, string message, long? recordId = null, string? path = null)
  {
    _errors.Add(new MutationError(code, message, path, recordId));
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
    return BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"air-lock:{lockedType.Name}:{numbers}")));
  }

  internal static async Task AcquireLocksAsync(PlatformDbContext? db, Type lockedType, IEnumerable<long[]> keys, CancellationToken ct)
  {
    if (db == null || db.Database.CurrentTransaction == null)
    {
      return;
    }

    var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    var isSqlServer = db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
    if (!isPostgres && !isSqlServer)
    {
      return;
    }

    var lockKeys = keys.Select(k => LockKeyOf(lockedType, k)).Distinct().OrderBy(k => k).ToList();
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
          $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;",
          ct);
      }
    }
  }
}
