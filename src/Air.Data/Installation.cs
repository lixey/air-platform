namespace Platform.Data;

public static class Installation
{
  public static Task<bool> IsDoneAsync(PlatformDbContext db, string module, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentException.ThrowIfNullOrWhiteSpace(module);
    return db.AppPlatformInstallation.AnyAsync(i => i.Module == module, cancellationToken);
  }

  public static async Task<bool> RunOnceAsync(
    PlatformDbContext db,
    string module,
    Func<CancellationToken, Task> seed,
    CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(seed);

    if (await IsDoneAsync(db, module, cancellationToken))
    {
      return false;
    }

    await seed(cancellationToken);
    db.AppPlatformInstallation.Add(new AppPlatformInstallation { Module = module, InstalledAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(cancellationToken);
    return true;
  }
}
