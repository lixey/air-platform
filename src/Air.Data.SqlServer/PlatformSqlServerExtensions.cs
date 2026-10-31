namespace Platform.Data.SqlServer;

public static class PlatformSqlServerExtensions
{
  public const string DefaultConnectionName = "DefaultConnection";

  public static DbContextOptionsBuilder UsePlatformSqlServer(
    this DbContextOptionsBuilder builder,
    string connectionString,
    Action<SqlServerDbContextOptionsBuilder>? sqlServer = null)
  {
    ArgumentNullException.ThrowIfNull(builder);
    ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
    QueryMap.UseCollationComparison = true;

    return builder.UseSqlServer(connectionString, o =>
    {
      o.EnableRetryOnFailure(3);
      sqlServer?.Invoke(o);
    });
  }

  public static DbContextOptionsBuilder<TContext> UsePlatformSqlServer<TContext>(
    this DbContextOptionsBuilder<TContext> builder,
    string connectionString,
    Action<SqlServerDbContextOptionsBuilder>? sqlServer = null)
    where TContext : DbContext =>
    (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)builder).UsePlatformSqlServer(connectionString, sqlServer);

  public static IHostApplicationBuilder AddPlatformSqlServer<TContext>(
    this IHostApplicationBuilder builder,
    string connectionName = DefaultConnectionName,
    Action<SqlServerDbContextOptionsBuilder>? sqlServer = null)
    where TContext : PlatformDbContext
  {
    ArgumentNullException.ThrowIfNull(builder);

    var connectionString = builder.Configuration.GetConnectionString(connectionName)
      ?? throw new InvalidOperationException($"Connection string '{connectionName}' was not found in configuration.");

    builder.Services.AddDbContext<TContext>(options =>
      options.UsePlatformSqlServer(connectionString, sqlServer));

    builder.Services.RegisterPlatformDbContextHierarchy<TContext>();

    return builder;
  }

  /// <summary>
  /// High-speed bulk insert into SQL Server preserving explicit entity IDs (<see cref="SqlBulkCopyOptions.KeepIdentity"/>)
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
    var list = new List<TEntity>();
    await foreach (var item in items.WithCancellation(ct))
    {
      list.Add(item);
    }

    return await db.BulkImportAsync((IReadOnlyList<TEntity>)list, ct);
  }

  /// <summary>
  /// High-speed bulk insert into SQL Server preserving explicit entity IDs (<see cref="SqlBulkCopyOptions.KeepIdentity"/>)
  /// and automatically reseeding the table's IDENTITY sequence above the maximum imported <see cref="IEntity.Id"/>.
  /// </summary>
  public static async Task<int> BulkImportAsync<TEntity>(
    this PlatformDbContext db,
    IReadOnlyList<TEntity> items,
    CancellationToken ct = default)
    where TEntity : class, IEntity
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(items);
    if (items.Count == 0)
    {
      return 0;
    }

    var entityType = db.Model.FindEntityType(typeof(TEntity))
      ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' is not registered in '{db.GetType().Name}'.");

    var tableName = entityType.GetTableName()
      ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' is not mapped to a table.");
    var schema = entityType.GetSchema();
    var storeObject = StoreObjectIdentifier.Table(tableName, schema);
    var qualifiedTable = string.IsNullOrWhiteSpace(schema)
      ? $"[{tableName}]"
      : $"[{schema}].[{tableName}]";

    var now = DateTimeOffset.UtcNow;
    var tenantId = db.TenantContext.TenantId;

    var properties = entityType.GetProperties()
      .Where(p => !p.IsShadowProperty() && p.PropertyInfo is not null && p.GetColumnName(storeObject) is not null)
      .Select(p => (
        Property: p,
        Getter: p.GetGetter(),
        ColumnName: p.GetColumnName(storeObject)!,
         p.GetTypeMapping().Converter
      ))
      .ToList();

    using var table = new DataTable(tableName);
    foreach (var prop in properties)
    {
      var colType = prop.Converter?.ProviderClrType ?? prop.Property.ClrType;
      colType = Nullable.GetUnderlyingType(colType) ?? colType;
      table.Columns.Add(prop.ColumnName, colType);
    }

    long maxId = 0;
    foreach (var item in items)
    {
      if (item is IHasTimestamps ts && ts.CreatedAt == default)
      {
        ts.CreatedAt = now;
      }

      if (item is IHasTenant tenantEntity)
      {
        if (tenantEntity.TenantId == 0 && tenantId.HasValue)
        {
          tenantEntity.TenantId = tenantId.Value;
        }
        else if (tenantId.HasValue && tenantEntity.TenantId != 0 && tenantEntity.TenantId != tenantId.Value)
        {
          throw new InvalidOperationException($"Cannot bulk import '{typeof(TEntity).Name}' belonging to another tenant.");
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

    var conn = (SqlConnection)db.Database.GetDbConnection();
    var wasClosed = conn.State != ConnectionState.Open;
    if (wasClosed)
    {
      await conn.OpenAsync(ct);
    }

    try
    {
      var currentTx = db.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
      using var bulk = new SqlBulkCopy(
        conn,
        SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock,
        currentTx)
      {
        DestinationTableName = qualifiedTable,
        BulkCopyTimeout = 300
      };

      foreach (var prop in properties)
      {
        bulk.ColumnMappings.Add(prop.ColumnName, prop.ColumnName);
      }

      await bulk.WriteToServerAsync(table, ct);

      if (maxId > 0)
      {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = currentTx;
        cmd.CommandText = """
          IF OBJECTPROPERTY(OBJECT_ID(@tbl), 'TableHasIdentity') = 1
          BEGIN
            DECLARE @cur bigint = ISNULL(IDENT_CURRENT(@tbl), 0);
            IF @maxId > @cur
              DBCC CHECKIDENT (@tbl, RESEED, @maxId) WITH NO_INFOMSGS;
          END
          """;
        cmd.Parameters.Add(new SqlParameter("@tbl", SqlDbType.NVarChar, 256) { Value = qualifiedTable });
        cmd.Parameters.Add(new SqlParameter("@maxId", SqlDbType.BigInt) { Value = maxId });
        await cmd.ExecuteNonQueryAsync(ct);
      }

      return items.Count;
    }
    finally
    {
      if (wasClosed)
      {
        await conn.CloseAsync();
      }
    }
  }
}
