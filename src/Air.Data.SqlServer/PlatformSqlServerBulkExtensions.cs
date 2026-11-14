using Platform.Data.SqlServer.Internals;

namespace Platform.Data.SqlServer;

public static class PlatformSqlServerBulkExtensions
{
  private const int BulkBatchSize = 5_000;

  /// <summary>
  /// High-speed bounded bulk insert into SQL Server preserving explicit entity IDs (<see cref="SqlBulkCopyOptions.KeepIdentity"/>)
  /// and automatically reseeding the table's IDENTITY sequence above the maximum imported <see cref="IEntity.Id"/>.
  /// </summary>
  public static async Task<int> BulkImportAsync<TEntity>(
    this PlatformDbContext db,
    IAsyncEnumerable<TEntity> items,
    CancellationToken ct = default)
    where TEntity : class, IEntity
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(items);

    var (tableName, qualifiedTable, properties) = ResolveBulkMetadata<TEntity>(db);
    var isSystemScope = db.TenantContext.IsSystemScope;
    if (typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)) && !isSystemScope)
    {
      throw new InvalidOperationException($"{typeof(TEntity).Name} is read-only.");
    }

    await using var enumerator = items.GetAsyncEnumerator(ct);
    if (!await enumerator.MoveNextAsync())
    {
      return 0;
    }

    using var table = CreateBulkDataTable(tableName, properties);
    var conn = (SqlConnection)db.Database.GetDbConnection();
    var wasClosed = conn.State != ConnectionState.Open;
    if (wasClosed)
    {
      await conn.OpenAsync(ct);
    }

    try
    {
      var currentTx = db.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
      using var bulk = CreateBulkCopy(conn, currentTx, qualifiedTable, properties);

      var now = db.Time.GetUtcNow();
      var tenantId = db.TenantContext.TenantId;
      var userId = db.TenantContext.UserId;
      long maxId = 0;
      var totalImported = 0;

      do
      {
        AppendBulkRow(typeof(TEntity).Name, enumerator.Current, table, properties, now, tenantId, userId, isSystemScope, ref maxId);
        totalImported++;

        if (table.Rows.Count >= BulkBatchSize)
        {
          await bulk.WriteToServerAsync(table, ct);
          table.Rows.Clear();
        }
      }
      while (await enumerator.MoveNextAsync());

      if (table.Rows.Count > 0)
      {
        await bulk.WriteToServerAsync(table, ct);
        table.Rows.Clear();
      }

      if (maxId > 0)
      {
        await ReseedIdentityAsync(conn, currentTx, qualifiedTable, maxId, ct);
      }

      return totalImported;
    }
    finally
    {
      if (wasClosed)
      {
        await conn.CloseAsync();
      }
    }
  }

  /// <summary>
  /// High-speed bounded bulk insert into SQL Server preserving explicit entity IDs (<see cref="SqlBulkCopyOptions.KeepIdentity"/>)
  /// and automatically reseeding the table's IDENTITY sequence above the maximum imported <see cref="IEntity.Id"/>.
  /// </summary>
  public static Task<int> BulkImportAsync<TEntity>(
    this PlatformDbContext db,
    IReadOnlyList<TEntity> items,
    CancellationToken ct = default)
    where TEntity : class, IEntity
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(items);
    return BulkImportAsync(db, items.ToAsyncEnumerable(), ct);
  }

  private static (string TableName, string QualifiedTable, List<SqlBulkColumnInfo> Properties)
    ResolveBulkMetadata<TEntity>(PlatformDbContext db)
  {
    var entityType = db.Model.FindEntityType(typeof(TEntity))
      ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' is not registered in '{db.GetType().Name}'.");

    var tableName = entityType.GetTableName()
      ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' is not mapped to a table.");
    var schema = entityType.GetSchema();
    var storeObject = StoreObjectIdentifier.Table(tableName, schema);
    var qualifiedTable = string.IsNullOrWhiteSpace(schema)
      ? $"[{tableName.Replace("]", "]]")}]"
      : $"[{schema.Replace("]", "]]")}].[{tableName.Replace("]", "]]")}]";

    var properties = entityType.GetProperties()
      .Where(p => !p.IsShadowProperty() &&
                  p.PropertyInfo is not null &&
                  p.GetColumnName(storeObject) is not null &&
                  p.GetComputedColumnSql(storeObject) is null &&
                  p.ValueGenerated is not ValueGenerated.OnAddOrUpdate)
      .Select(p => new SqlBulkColumnInfo(
        p,
        p.GetGetter(),
        p.GetColumnName(storeObject)!,
        p.GetTypeMapping().Converter))
      .ToList();

    return (tableName, qualifiedTable, properties);
  }

  private static DataTable CreateBulkDataTable(string tableName, List<SqlBulkColumnInfo> properties)
  {
    var table = new DataTable(tableName) { MinimumCapacity = BulkBatchSize };
    foreach (var prop in properties)
    {
      var colType = prop.Converter?.ProviderClrType ?? prop.Property.ClrType;
      colType = Nullable.GetUnderlyingType(colType) ?? colType;
      table.Columns.Add(prop.ColumnName, colType);
    }

    return table;
  }

  private static SqlBulkCopy CreateBulkCopy(
    SqlConnection conn,
    SqlTransaction? currentTx,
    string qualifiedTable,
    List<SqlBulkColumnInfo> properties)
  {
    var bulk = new SqlBulkCopy(
      conn,
      SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock,
      currentTx)
    {
      DestinationTableName = qualifiedTable,
      BatchSize = BulkBatchSize,
      BulkCopyTimeout = 300
    };

    foreach (var prop in properties)
    {
      bulk.ColumnMappings.Add(prop.ColumnName, prop.ColumnName);
    }

    return bulk;
  }

  private static void AppendBulkRow<TEntity>(
    string entityName,
    TEntity item,
    DataTable table,
    List<SqlBulkColumnInfo> properties,
    DateTimeOffset now,
    long? tenantId,
    long? userId,
    bool isSystemScope,
    ref long maxId)
    where TEntity : class, IEntity
  {
    ArgumentNullException.ThrowIfNull(item);

    if (!isSystemScope)
    {
      if (item is BaseAppUser { IsRoot: true })
      {
        throw new InvalidOperationException("Only system scope may set the root user flag.");
      }

      if (item is AppAgentMemory mem)
      {
        if (string.Equals(mem.Kind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase))
        {
          throw new InvalidOperationException("Seed AppAgentMemory rows can only be written in system scope.");
        }

        if (mem.UserId is null or 0)
        {
          var isSharedKind = string.Equals(mem.Kind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase);
          mem.UserId = isSharedKind
            ? null
            : userId ?? throw new InvalidOperationException("Cannot bulk import personal AppAgentMemory without an active user.");
        }
        else if (mem.UserId != userId)
        {
          throw new InvalidOperationException("Cannot bulk import AppAgentMemory belonging to another user.");
        }
      }
    }

    if (item is IHasTimestamps ts && ts.CreatedAt == default)
    {
      ts.CreatedAt = now;
    }

    if (item is IHasTenant tenantEntity)
    {
      if (!isSystemScope)
      {
        if (!tenantId.HasValue)
        {
          throw new InvalidOperationException($"Cannot bulk import '{entityName}' without an active tenant.");
        }

        if (tenantEntity.TenantId == 0)
        {
          tenantEntity.TenantId = tenantId.Value;
        }
        else if (tenantEntity.TenantId != tenantId.Value)
        {
          throw new InvalidOperationException($"Cannot bulk import '{entityName}' belonging to another tenant.");
        }
      }
      else if (tenantEntity.TenantId == 0)
      {
        tenantEntity.TenantId = tenantId ?? throw new InvalidOperationException(
          $"Cannot bulk import '{entityName}' without an explicit TenantId.");
      }
    }

    if (item is IHasOptionalTenant optionalTenant)
    {
      if (!isSystemScope)
      {
        if (!tenantId.HasValue)
        {
          throw new InvalidOperationException($"Cannot bulk import '{entityName}' without an active tenant.");
        }

        if (optionalTenant.TenantId is null or 0)
        {
          optionalTenant.TenantId = tenantId.Value;
        }
        else if (optionalTenant.TenantId != tenantId.Value)
        {
          throw new InvalidOperationException($"Cannot bulk import '{entityName}' belonging to another tenant.");
        }
      }
      else if (optionalTenant.TenantId == 0)
      {
        optionalTenant.TenantId = tenantId;
      }
    }

    if (item is IHasVersion ver && ver.Ver == 0)
    {
      ver.Ver = 1;
    }

    if (item.Id > maxId)
    {
      maxId = item.Id;
    }

    var row = table.NewRow();
    foreach (var prop in properties)
    {
      var raw = prop.Getter.GetClrValue(item);
      if (raw is not null && prop.Converter is not null)
      {
        raw = prop.Converter.ConvertToProvider(raw);
      }

      row[prop.ColumnName] = raw ?? DBNull.Value;
    }

    table.Rows.Add(row);
  }

  private static async Task ReseedIdentityAsync(
    SqlConnection conn,
    SqlTransaction? currentTx,
    string qualifiedTable,
    long maxId,
    CancellationToken ct)
  {
    await using var cmd = conn.CreateCommand();
    cmd.Transaction = currentTx;
    cmd.CommandText = """
      IF OBJECTPROPERTY(OBJECT_ID(@tbl), 'TableHasIdentity') = 1
      BEGIN
        DECLARE @cur bigint = ISNULL(IDENT_CURRENT(@tbl), 0);
        IF @maxId >= @cur
          DBCC CHECKIDENT (@tbl, RESEED, @maxId) WITH NO_INFOMSGS;
      END
      """;
    cmd.Parameters.Add(new SqlParameter("@tbl", SqlDbType.NVarChar, 520) { Value = qualifiedTable });
    cmd.Parameters.Add(new SqlParameter("@maxId", SqlDbType.BigInt) { Value = maxId });
    await cmd.ExecuteNonQueryAsync(ct);
  }
}
