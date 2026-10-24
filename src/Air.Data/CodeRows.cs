using System.Text.RegularExpressions;

namespace Platform.Data;

public static partial class CodeRows
{
  private static readonly HashSet<string> Kept =
  [
    nameof(BaseEntity.Ver),
    nameof(BaseEntity.CreatedAt),
    nameof(BaseEntity.UpdatedAt)
  ];

  [GeneratedRegex("^[A-Z][a-zA-Z0-9]*$", RegexOptions.CultureInvariant)]
  private static partial Regex PascalCaseRegex();

  public static void EnsurePascalCaseHandle(string? handle, string entityName)
  {
    if (string.IsNullOrWhiteSpace(handle) ||
        !PascalCaseRegex().IsMatch(handle) ||
        handle.Length > 1 && handle.All(char.IsUpper))
    {
      throw new InvalidOperationException(
        $"Entity '{entityName}' has invalid Handle '{handle}'. Handle must always be a PascalCase identifier (use a separate property like Code for non-PascalCase values)."
      );
    }
  }

  public static async Task SyncAsync<TEntity>(DbContext db, IReadOnlyCollection<TEntity> rows, CancellationToken cancellationToken = default)
    where TEntity : class, IEntity
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(rows);

    foreach (var row in rows)
    {
      if (row is BaseDictionaryEntity dict)
      {
        EnsurePascalCaseHandle(dict.Handle, typeof(TEntity).Name);
      }
    }

    var set = db.Set<TEntity>();
    var existing = await set.IgnoreQueryFilters().ToDictionaryAsync(r => r.Id, cancellationToken);
    var properties = db.Model.FindEntityType(typeof(TEntity))!.GetProperties()
      .Where(p => !p.IsPrimaryKey() && !p.IsShadowProperty() && p.PropertyInfo is not null && !Kept.Contains(p.Name))
      .ToList();

    foreach (var row in rows)
    {
      if (!existing.TryGetValue(row.Id, out var current))
      {
        set.Add(row);
        continue;
      }

      var entry = db.Entry(current);
      foreach (var property in properties)
      {
        var nextVal = property.PropertyInfo!.GetValue(row);
        if (!Equals(entry.Property(property.Name).CurrentValue, nextVal))
        {
          entry.Property(property.Name).CurrentValue = nextVal;
        }
      }
    }

    if (db.ChangeTracker.HasChanges())
    {
      var isSqlServer = db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
      var entityType = db.Model.FindEntityType(typeof(TEntity));
      var idProp = entityType?.FindPrimaryKey()?.Properties.FirstOrDefault();
      var hasIdentityColumn = idProp?.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd;
      var hasExplicitInsert = isSqlServer && hasIdentityColumn && db.ChangeTracker.Entries<TEntity>().Any(e => e.State == EntityState.Added && e.Entity.Id != 0);
      if (hasExplicitInsert && entityType != null)
      {
        var tableName = entityType.GetTableName()!;
        var schema = entityType.GetSchema();
        var qualifiedTable = string.IsNullOrWhiteSpace(schema) ? $"[{tableName}]" : $"[{schema}].[{tableName}]";
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
          await db.Database.OpenConnectionAsync(cancellationToken);
          try
          {
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT {qualifiedTable} ON", cancellationToken);
            try
            {
              await db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
              await db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT {qualifiedTable} OFF", cancellationToken);
            }
#pragma warning restore EF1002
          }
          finally
          {
            await db.Database.CloseConnectionAsync();
          }
        });
      }
      else
      {
        await db.SaveChangesAsync(cancellationToken);
      }
    }
  }
}
