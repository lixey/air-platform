using Platform.App.Internals;

namespace Platform.App.Changes;

/// <summary>
/// The change journal (AppPlatformChange) of the tenant: a grid an application routes with a sealed
/// service of its own and a read-only feature.
/// </summary>
public abstract partial class ChangeGridService(PlatformDbContext db, IServiceProvider? services = null, PlatformAppService? app = null)
  : GridService<AppPlatformChange>(db, app)
{
  private (string? Service, long? Id) _validatedParentScope;

  protected override bool IsReadOnly => true;

  protected override void SetParent() => Parent.AllowDynamicParent();

  public override bool CanView(GridRequest request, IUserContext? user)
  {
    if (user is not { IsAuthenticated: true })
    {
      return false;
    }

    if (!string.IsNullOrEmpty(request.ParentService) || request.ParentId.HasValue)
    {
      if (string.IsNullOrEmpty(request.ParentService) || request.ParentId is not > 0)
      {
        return false;
      }

      var parentFeat = Catalog.FindFeature(request.ParentService);
      if (parentFeat == null &&
          services != null &&
          Catalog.FindServiceType(request.ParentService) is { } parentServiceType)
      {
        parentFeat = (services.GetService(parentServiceType) as IPlatformService)?.Feature;
      }

      if (parentFeat == null || !ServiceEndpointsExtensions.CanView(parentFeat, user))
      {
        return false;
      }

      if (base.CanView(new GridRequest(), user))
      {
        return true;
      }

      return parentFeat.HasHistory &&
             (user.Can(parentFeat.History) ||
              user.Can(parentFeat.All) ||
              user.Can("*"));
    }

    return base.CanView(request, user);
  }

  public override bool CanLookup(GridRequest request, IUserContext? user) =>
    CanView(request, user);

  protected override async Task<bool> EnsureParentScopeAsync(bool lockParent, CancellationToken ct)
  {
    _validatedParentScope = default;

    if (string.IsNullOrEmpty(ParentService) && !ParentId.HasValue)
    {
      return base.CanView(new GridRequest(), User);
    }

    if (string.IsNullOrEmpty(ParentService) || ParentId is not > 0)
    {
      return false;
    }

    var scopedReq = new GridRequest { ParentService = ParentService, ParentId = ParentId };
    if (!CanView(scopedReq, User))
    {
      return false;
    }

    var parentServiceType = ModuleCatalog.Instance.FindServiceType(ParentService);
    var entityType = ModuleCatalog.GetServiceEntityType(parentServiceType);
    if (parentServiceType == null || parentServiceType.IsAbstract || entityType == null)
    {
      return false;
    }

    var targetDb = GetDbContextForTable(entityType.Name);
    if (typeof(Form.IFormService).IsAssignableFrom(parentServiceType))
    {
      var (allowed, _, _) = await Internals.ParentScopeValidator.ValidateAndLoadAsync(
        targetDb,
        PlatformApp,
        ParentId,
        ParentService,
        hasRequiredBoundParent: true,
        _ => true,
        ParentService,
        parentServiceType,
        entityType,
        lockParent: false,
        ct);
      if (!allowed)
      {
        return false;
      }

      _validatedParentScope = (ParentService, ParentId);
      return true;
    }

    if (typeof(IGridService).IsAssignableFrom(parentServiceType) &&
        ModuleCatalog.Instance.CreateServiceInstance(parentServiceType, targetDb, PlatformApp, services) is IGridService parentGrid)
    {
      if (!parentGrid.CanView(new GridRequest(), User))
      {
        return false;
      }

      var row = await parentGrid.ReadRowAsync(ParentId.Value, ct);
      if (row == null)
      {
        return false;
      }

      _validatedParentScope = (ParentService, ParentId);
      return true;
    }

    return false;
  }

  protected override OverlayDto? ResolveFormOverlay()
  {
    var servicePath = ServicePathResolver.Resolve(GetType());
    return OverlayModel.Dialog($"{servicePath}.diff")
      .Screen(servicePath)
      .Width(560)
      .Build();
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).Cell(new GridCellOpenFormModel()).Width(90);
    Columns.Add(r => r.At).AsDateTime();

    var userGridType = ResolveUserGridServiceType(Catalog);
    if (userGridType != null)
    {
      ReferenceLookupAllowlist.RegisterReferrer(userGridType, GetType());
      ReferenceLookupAllowlist.RegisterLabels(userGridType, ["displayName", "login", "name"]);
      ReferenceLookupAllowlist.RegisterFilterField(userGridType, "id");
    }

    var userCol = Columns.Add(r => r.UserId).Label("User");
    userCol.Dto.Type = UiFieldType.Text;
    userCol.Dto.Cell = new GridCellReferenceModel();
    userCol.Dto.Header = new GridHeaderReferenceModel();
    userCol.Dto.ReferenceService = userGridType != null ? ServicePathResolver.Resolve(userGridType) : null;
    userCol.Dto.FilterPath = "userId";

    Columns.Add(r => r.Operation);
    if (string.IsNullOrEmpty(ParentService))
    {
      Columns.Add(r => r.Table);
      Columns.Add(r => r.RowId);
    }

    Columns.Add(r => r.Before);
    Columns.Add(r => r.After);
    Columns.Add(r => r.CorrelationId);
  }

  protected override void SetQuery()
  {
    var hasGlobalView = base.CanView(new GridRequest(), User);
    if (!string.IsNullOrEmpty(ParentService) || ParentId.HasValue)
    {
      var isValidated = _validatedParentScope.Id == ParentId &&
                        string.Equals(_validatedParentScope.Service, ParentService, StringComparison.OrdinalIgnoreCase);
      var tableName = isValidated && !string.IsNullOrEmpty(ParentService)
        ? ResolveParentTableName(ParentService)
        : string.Empty;
      if (!isValidated || string.IsNullOrEmpty(tableName) || ParentId is not > 0)
      {
        Query.Where(_ => false);
      }
      else
      {
        var rowId = ParentId.Value;
        Query.Where(r => r.Table == tableName && r.RowId == rowId);
      }
    }
    else if (!hasGlobalView)
    {
      Query.Where(_ => false);
    }

    Query.NamedFilter("operation")
      .All("All").Default()
      .Item("added", "Added", r => r.Operation == ChangeOperations.Added).Color(UiColor.Green)
      .Item("modified", "Modified", r => r.Operation == ChangeOperations.Modified).Color(UiColor.Amber)
      .Item("deleted", "Deleted", r => r.Operation == ChangeOperations.Deleted).Color(UiColor.Red);

    Query.SortByDescending(r => r.At).ThenByDescending(r => r.Id);
    Query.SearchBy(
      r => r.Id,
      r => r.RowId,
      r => r.Table,
      r => r.Operation,
      r => r.Before,
      r => r.After,
      r => r.CorrelationId
    );
    Query.Compute(FillChangesAsync);
  }

  private async Task FillChangesAsync(IReadOnlyList<GridComputedRow<AppPlatformChange>> rows, CancellationToken ct)
  {
    var userIds = rows
      .Select(r => r.Record.UserId)
      .Where(id => id is > 0)
      .Select(id => id!.Value)
      .Distinct()
      .ToHashSet();

    var userNames = userIds.Count > 0 ? await db.LoadUserNamesAsync(userIds, ct) : [];

    foreach (var row in rows)
    {
      var e = row.Record;
      if (row.Values.ContainsKey("userId"))
      {
        if (e.UserId is > 0 and var uid)
        {
          var name = userNames.TryGetValue(uid, out var n) && !string.IsNullOrWhiteSpace(n) ? n : $"#{uid}";
          row["userId"] = new Reference { Id = uid, Name = name };
        }
        else
        {
          row["userId"] = null;
        }
      }

      if (row.Values.ContainsKey("operation") && !string.IsNullOrEmpty(e.Operation))
      {
        row["operation"] = L[e.Operation];
      }

      var onlyDiff = e.Operation == ChangeOperations.Modified;
      if (row.Values.ContainsKey("before") && e.Before != null)
      {
        row["before"] = FormatCompactAuditJson(e.Before, e.After, onlyDiff);
      }

      if (row.Values.ContainsKey("after") && e.After != null)
      {
        row["after"] = FormatCompactAuditJson(e.After, e.Before, onlyDiff);
      }

      if (row.Values.ContainsKey("correlationId") && !string.IsNullOrEmpty(e.CorrelationId))
      {
        row["correlationId"] = FormatCorrelationLabel(e.CorrelationId, L);
      }
    }
  }

  private string ResolveParentTableName(string parentService)
  {
    var serviceType = Catalog.FindServiceType(parentService);
    var entityType = ModuleCatalog.GetServiceEntityType(serviceType);
    if (entityType == null)
    {
      return string.Empty;
    }

    var targetDb = GetDbContextForTable(entityType.Name);
    var efEntity = targetDb.Model.FindEntityType(entityType);
    return efEntity?.GetTableName() ?? entityType.Name;
  }

  private static Type? ResolveUserGridServiceType(ModuleCatalog catalog)
  {
    foreach (var feat in catalog.AllFeatures)
    {
      if (feat.ServiceType != null &&
          typeof(IGridService).IsAssignableFrom(feat.ServiceType) &&
          ModuleCatalog.GetServiceEntityType(feat.ServiceType) is { } entityType &&
          typeof(BaseAppUser).IsAssignableFrom(entityType))
      {
        return feat.ServiceType;
      }
    }

    return null;
  }

  private PlatformDbContext GetDbContextForTable(string table)
  {
    if (db.Model.GetEntityTypes().Any(e => e.ClrType.Name == table || e.GetTableName() == table))
    {
      return db;
    }

    if ((services ?? PlatformApp?.Services) is { } sp)
    {
      foreach (var dbType in Catalog.DbContextTypes)
      {
        if (dbType != db.GetType() &&
            sp.GetService(dbType) is PlatformDbContext otherDb &&
            otherDb.Model.GetEntityTypes().Any(e => e.ClrType.Name == table || e.GetTableName() == table))
        {
          return otherDb;
        }
      }
    }

    return db;
  }
}
