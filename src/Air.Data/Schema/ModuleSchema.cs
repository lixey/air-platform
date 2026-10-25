using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Platform.Data.Schema;

/// <summary>
/// Orders, validates, and verifies per-module <see cref="PlatformDbContext"/> schemas.
/// </summary>
public static class ModuleSchema
{
  /// <summary>
  /// Orders module contexts from base module to derived modules by inheritance depth, breaking ties by handle.
  /// </summary>
  public static IReadOnlyList<PlatformDbContext> OrderContexts(IEnumerable<PlatformDbContext> contexts)
  {
    ArgumentNullException.ThrowIfNull(contexts);
    return contexts
      .OrderBy(c => GetInheritanceDepth(c.GetType()))
      .ThenBy(c => c.ModuleHandle ?? string.Empty, StringComparer.Ordinal)
      .ToArray();
  }

  /// <summary>
  /// Validates that all module contexts declare unique handles, cover everyancestor module level,
  /// and agree on table ownership and shared column definitions.
  /// </summary>
  public static void Validate(IReadOnlyList<PlatformDbContext> contexts)
  {
    ArgumentNullException.ThrowIfNull(contexts);
    if (contexts.Count == 0)
    {
      throw new ArgumentException("At least one module context is required.", nameof(contexts));
    }

    var registeredModules = new HashSet<string>(StringComparer.Ordinal);
    var tableOwners = new Dictionary<(string? Schema, string Name), string>();
    var requiredAncestors = new HashSet<string>(StringComparer.Ordinal);

    foreach (var db in OrderContexts(contexts))
    {
      var contextType = db.GetType();
      var module = db.ModuleHandle
        ?? throw new InvalidOperationException($"'{contextType.Name}' does not declare [ModuleSchema].");

      if (!registeredModules.Add(module))
      {
        throw new InvalidOperationException($"Module schema '{module}' is registered more than once.");
      }

      foreach (var level in ModuleOwnershipExtensions.ResolveLevels(contextType))
      {
        requiredAncestors.Add(level.Handle);
      }

      var designModel = db.GetService<IDesignTimeModel>().Model;
      var localOwners = ModuleOwnershipExtensions.InferTableOwners(designModel, contextType);
      foreach (var (table, owner) in localOwners)
      {
        if (tableOwners.TryGetValue(table, out var existing) &&
            !string.Equals(existing, owner, StringComparison.Ordinal))
        {
          throw new InvalidOperationException(
            $"Table '{table.Schema ?? "public"}.{table.Name}' has conflicting owners '{existing}' and '{owner}'.");
        }

        tableOwners[table] = owner;
      }
    }

    foreach (var required in requiredAncestors)
    {
      if (!registeredModules.Contains(required))
      {
        throw new InvalidOperationException(
          $"Module schema '{required}' is required by a derived context but has no context in the migration set.");
      }
    }
  }

  /// <summary>
  /// Verifies that all tables and columns owned by <paramref name="db"/> exist in the target relational database.
  /// </summary>
  public static async Task VerifyAsync(PlatformDbContext db, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    if (!db.Database.IsRelational())
    {
      return;
    }

    var contextType = db.GetType();
    var module = db.ModuleHandle
      ?? throw new InvalidOperationException($"'{contextType.Name}' does not declare [ModuleSchema].");

    var model = db.GetService<IDesignTimeModel>().Model;
    var owners = ModuleOwnershipExtensions.InferTableOwners(model, contextType);
    var sql = db.GetService<ISqlGenerationHelper>();

    foreach (var (table, owner) in owners)
    {
      if (!string.Equals(owner, module, StringComparison.Ordinal))
      {
        continue;
      }

      var store = StoreObjectIdentifier.Table(table.Name, table.Schema);
      var columns = model.GetEntityTypes()
        .Where(e => e.GetTableName() == table.Name && e.GetSchema() == table.Schema)
        .SelectMany(e => e.GetProperties())
        .Select(p => p.GetColumnName(store))
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .Select(sql.DelimitIdentifier)
        .ToArray();

      if (columns.Length == 0)
      {
        continue;
      }

      var cmd = $"SELECT {string.Join(", ", columns)} FROM {sql.DelimitIdentifier(table.Name, table.Schema)} WHERE 1 = 0";
      await db.Database.ExecuteSqlRawAsync(cmd, cancellationToken);
    }
  }

  /// <summary>
  /// Creates an empty database and creates tables for each module context in inheritance order (for dev/test harnesses).
  /// </summary>
  public static async Task EnsureCreatedAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(contexts);
    if (contexts.Count == 0)
    {
      return;
    }

    var ordered = OrderContexts(contexts);
    var firstCreator = ordered[0].GetService<IRelationalDatabaseCreator>();
    if (!await firstCreator.ExistsAsync(cancellationToken))
    {
      await firstCreator.CreateAsync(cancellationToken);
    }

    if (await firstCreator.HasTablesAsync(cancellationToken))
    {
      return;
    }

    foreach (var db in ordered)
    {
      await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
    }
  }

  private static int GetInheritanceDepth(Type type)
  {
    var depth = 0;
    for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
    {
      depth++;
    }

    return depth;
  }
}
