namespace Platform.App.Changes;

/// <summary>
/// The change journal (AppPlatformChange) of the tenant: a grid an application routes with a sealed
/// service of its own and a read-only feature.
/// </summary>
public abstract partial class ChangeGridService(PlatformDbContext db, IServiceProvider? services = null, PlatformAppService? app = null) : GridService<AppPlatformChange>(db, app)
{
  protected override bool IsReadOnly => true;

  public override bool CanView(GridRequest request, IUserContext? user)
  {
    if (user is not { IsAuthenticated: true })
    {
      return false;
    }

    if (base.CanView(request, user))
    {
      return true;
    }

    if (!string.IsNullOrEmpty(request.ParentService) &&
        request.ParentId is > 0)
    {
      var parentFeat = ModuleCatalog.Instance.FindFeature(request.ParentService);
      if (parentFeat == null &&
          services != null &&
          ModuleCatalog.Instance.FindServiceType(request.ParentService) is { } parentServiceType)
      {
        parentFeat = (services.GetService(parentServiceType) as IPlatformService)?.Feature;
      }

      if (parentFeat is { HasHistory: true })
      {
        return user.Can(parentFeat.History) ||
               user.Can(parentFeat.All) ||
               user.Can("*");
      }
    }

    return false;
  }

  public override bool CanLookup(GridRequest request, IUserContext? user) =>
    CanView(request, user);

  protected override OverlayDto? ResolveFormOverlay(Columns columns)
  {
    var servicePath = ServicePathResolver.Resolve(GetType());
    return OverlayModel.Dialog($"{servicePath}.diff")
      .Screen(servicePath)
      .Width(560)
      .Build();
  }

  protected override void SetColumns(Columns<AppPlatformChange> columns)
  {
    columns.Add(r => r.Id).Cell(new GridCellOpenFormModel()).Width(90);
    columns.Add(r => r.At).AsDatetime();

    var userCol = columns.Add(r => r.UserId).Label("User");
    userCol.Dto.Type = UiFieldType.Text;
    userCol.Dto.Cell = new GridCellReferenceModel();
    userCol.Dto.Header = new GridHeaderReferenceModel();
    userCol.Dto.ReferenceService = ResolveUserGridServicePath();
    userCol.Dto.FilterPath = "userId";

    columns.Add(r => r.Operation);
    if (string.IsNullOrEmpty(ParentService))
    {
      columns.Add(r => r.Table);
      columns.Add(r => r.RowId);
    }

    columns.Add(r => r.Before);
    columns.Add(r => r.After);
    columns.Add(r => r.CorrelationId);
  }

  protected override void SetQuery(Query query)
  {
    if (!string.IsNullOrEmpty(ParentService) && ParentId is > 0)
    {
      var tableName = ResolveParentTableName(ParentService);
      var rowId = ParentId.Value;
      query.Where(r => r.Table == tableName && r.RowId == rowId);
    }

    query.NamedFilter("operation")
      .All("All").Default()
      .Item("added", "Added", r => r.Operation == ChangeOperations.Added).Color(UiColor.Green)
      .Item("modified", "Modified", r => r.Operation == ChangeOperations.Modified).Color(UiColor.Amber)
      .Item("deleted", "Deleted", r => r.Operation == ChangeOperations.Deleted).Color(UiColor.Red);

    query.SortByDescending(r => r.At).ThenByDescending(r => r.Id);
    query.SearchBy(r => r.Id, r => r.RowId, r => r.Table, r => r.Operation, r => r.Before, r => r.After, r => r.CorrelationId);
  }

  public override async Task<GridPageDto<object>> GetDataAsync(ListRequest request, CancellationToken ct = default)
  {
    var page = await base.GetDataAsync(request, ct);
    if (page.Rows.Count == 0)
    {
      return page;
    }

    var userIds = new HashSet<long>();
    foreach (var rowObj in page.Rows)
    {
      if (rowObj is Dictionary<string, object?> dict &&
          dict.TryGetValue("userId", out var uidObj) &&
          uidObj != null &&
          long.TryParse(uidObj.ToString(), out var uid) &&
          uid > 0)
      {
        userIds.Add(uid);
      }
    }

    var userNames = userIds.Count > 0 ? await LoadUserNamesAsync(userIds, ct) : [];

    foreach (var rowObj in page.Rows)
    {
      if (rowObj is Dictionary<string, object?> dict)
      {
        if (dict.TryGetValue("userId", out var uidObj) &&
            uidObj != null &&
            long.TryParse(uidObj.ToString(), out var uid) &&
            uid > 0)
        {
          var name = userNames.TryGetValue(uid, out var n) && !string.IsNullOrWhiteSpace(n) ? n : $"#{uid}";
          dict["userId"] = new Reference { Id = uid, Name = name };
        }
        else
        {
          dict["userId"] = null;
        }
      }
    }

    return page;
  }

  protected override object ShapeRow(
    AppPlatformChange row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<AppPlatformChange, bool> Predicate)>? rowFlags = null)
  {
    var shaped = base.ShapeRow(row, columns, rowFlags);
    if (shaped is Dictionary<string, object?> dict)
    {
      if (dict.TryGetValue("operation", out var opObj) && opObj is string opStr && !string.IsNullOrEmpty(opStr))
      {
        dict["operation"] = L[opStr];
      }

      var onlyDiff = row.Operation == ChangeOperations.Modified;

      if (dict.TryGetValue("before", out var beforeObj) && beforeObj is string beforeStr)
      {
        dict["before"] = FormatCompactAuditJson(beforeStr, row.After, onlyDiff);
      }

      if (dict.TryGetValue("after", out var afterObj) && afterObj is string afterStr)
      {
        dict["after"] = FormatCompactAuditJson(afterStr, row.Before, onlyDiff);
      }
    }

    return shaped;
  }

  private string ResolveParentTableName(string parentService)
  {
    var serviceType = ModuleCatalog.Instance.FindServiceType(parentService);
    var entityType = ModuleCatalog.GetServiceEntityType(serviceType);
    if (entityType == null)
    {
      return string.Empty;
    }

    var targetDb = GetDbContextForTable(entityType.Name);
    var efEntity = targetDb.Model.FindEntityType(entityType);
    return efEntity?.GetTableName() ?? entityType.Name;
  }

  private static string? ResolveUserGridServicePath()
  {
    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (feat.ServiceType != null &&
          typeof(IGridService).IsAssignableFrom(feat.ServiceType) &&
          ModuleCatalog.GetServiceEntityType(feat.ServiceType) is { } entityType &&
          typeof(BaseAppUser).IsAssignableFrom(entityType))
      {
        return ServicePathResolver.Resolve(feat.ServiceType);
      }
    }

    return null;
  }

  private async Task<Dictionary<long, string>> LoadUserNamesAsync(IReadOnlyCollection<long> userIds, CancellationToken ct)
  {
    var result = new Dictionary<long, string>();
    if (userIds.Count == 0 || db.SetOfBase<BaseAppUser>() is not { } userQuery)
    {
      return result;
    }

    try
    {
      var rows = await userQuery
        .AsNoTracking()
        .Where(u => userIds.Contains(u.Id))
        .Select(u => new { u.Id, u.Name, u.Login })
        .ToListAsync(ct);

      foreach (var r in rows)
      {
        result[r.Id] = !string.IsNullOrWhiteSpace(r.Name) ? r.Name : r.Login;
      }
    }
    catch { }

    return result;
  }

  private PlatformDbContext GetDbContextForTable(string table)
  {
    if (db.Model.GetEntityTypes().Any(e => e.ClrType.Name == table || e.GetTableName() == table))
    {
      return db;
    }

    var sp = services ?? PlatformApp?.Services;
    if (sp != null)
    {
      var dbTypes = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a =>
        {
          try
          {
            return a.GetTypes();
          }
          catch
          {
            return Type.EmptyTypes;
          }
        })
        .Where(t => typeof(PlatformDbContext).IsAssignableFrom(t) && !t.IsAbstract && t != db.GetType());

      foreach (var dbType in dbTypes)
      {
        if (sp.GetService(dbType) is PlatformDbContext otherDb)
        {
          if (otherDb.Model.GetEntityTypes().Any(e => e.ClrType.Name == table || e.GetTableName() == table))
          {
            return otherDb;
          }
        }
      }
    }

    return db;
  }
}
