using Microsoft.EntityFrameworkCore.Metadata;
using Platform.App.Changes.Internals;
using Platform.App.Internals;

namespace Platform.App.Changes;

/// <summary>
/// The change journal (AppPlatformChange) of the tenant: a grid an application routes with a sealed
/// service of its own and a read-only feature.
/// </summary>
public abstract partial class ChangeGridService(PlatformDbContext db, PlatformAppService? app = null)
  : GridService<AppPlatformChange>(db, app)
{
  private const string ValidatedParentScopeKey = "ChangeGrid.ValidatedParentScope";
  private const string TableMetaCacheKey = "ChangeGrid.TableMetaCache";

  protected ChangeGridService(PlatformDbContext db, IServiceProvider? services, PlatformAppService? app = null)
    : this(db, app)
  {
    Services = services;
  }

  private IServiceProvider? Services => PlatformApp?.Services ?? field;

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
          Services is { } sp &&
          Catalog.FindServiceType(request.ParentService) is { } parentServiceType)
      {
        parentFeat = (sp.GetService(parentServiceType) as IPlatformService)?.Feature;
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
    SetExecutionItem<(string? Service, long? Id)>(ValidatedParentScopeKey, default);

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

    var parentServiceType = Catalog.FindServiceType(ParentService);
    var entityType = ModuleCatalog.GetServiceEntityType(parentServiceType);
    if (parentServiceType == null || parentServiceType.IsAbstract || entityType == null)
    {
      return false;
    }

    var (targetDb, _) = ResolveTableMetadata(entityType.Name);
    if (typeof(Form.IFormService).IsAssignableFrom(parentServiceType))
    {
      var (allowed, _, _) = await ParentScopeValidator.ValidateAndLoadAsync(
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

      SetExecutionItem(ValidatedParentScopeKey, (ParentService, ParentId));
      return true;
    }

    if (typeof(IGridService).IsAssignableFrom(parentServiceType) &&
        Catalog.CreateServiceInstance(parentServiceType, targetDb, PlatformApp, Services) is IGridService parentGrid)
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

      SetExecutionItem(ValidatedParentScopeKey, (ParentService, ParentId));
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
      var validatedParent = GetExecutionItem<(string? Service, long? Id)>(ValidatedParentScopeKey);
      var isValidated = validatedParent.Id == ParentId &&
                        string.Equals(validatedParent.Service, ParentService, StringComparison.OrdinalIgnoreCase);
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

    Query.NamedFilter("Operation")
      .All("All").Default()
      .Item("Added", "Added", r => r.Operation == ChangeOperations.Added).Color(UiColor.Green)
      .Item("Modified", "Modified", r => r.Operation == ChangeOperations.Modified).Color(UiColor.Amber)
      .Item("Deleted", "Deleted", r => r.Operation == ChangeOperations.Deleted).Color(UiColor.Red);

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
        row["before"] = ChangeAuditFormatter.FormatCompactAuditJson(e.Before, e.After, onlyDiff);
      }

      if (row.Values.ContainsKey("after") && e.After != null)
      {
        row["after"] = ChangeAuditFormatter.FormatCompactAuditJson(e.After, e.Before, onlyDiff);
      }

      if (row.Values.ContainsKey("correlationId") && !string.IsNullOrEmpty(e.CorrelationId))
      {
        row["correlationId"] = ChangeAuditFormatter.FormatCorrelationLabel(e.CorrelationId, L);
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

    var (targetDb, _) = ResolveTableMetadata(entityType.Name);
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

  private (PlatformDbContext Db, IEntityType? EntityType) ResolveTableMetadata(string table)
  {
    var cache = GetExecutionItem<Dictionary<string, (PlatformDbContext Db, IEntityType? EntityType)>>(TableMetaCacheKey);
    if (cache is null)
    {
      cache = new Dictionary<string, (PlatformDbContext Db, IEntityType? EntityType)>(StringComparer.Ordinal);
      SetExecutionItem(TableMetaCacheKey, cache);
    }

    if (cache.TryGetValue(table, out var cached))
    {
      return cached;
    }

    var localEntity = db.Model.GetEntityTypes()
      .FirstOrDefault(e => e.ClrType.Name == table || e.GetTableName() == table);
    if (localEntity != null)
    {
      var hit = (db, (IEntityType?)localEntity);
      cache[table] = hit;
      return hit;
    }

    if (Services is { } sp)
    {
      foreach (var dbType in Catalog.DbContextTypes)
      {
        if (dbType != db.GetType() && sp.GetService(dbType) is PlatformDbContext otherDb)
        {
          var otherEntity = otherDb.Model.GetEntityTypes()
            .FirstOrDefault(e => e.ClrType.Name == table || e.GetTableName() == table);
          if (otherEntity != null)
          {
            var hit = (otherDb, (IEntityType?)otherEntity);
            cache[table] = hit;
            return hit;
          }
        }
      }
    }

    var fallback = (db, (IEntityType?)null);
    cache[table] = fallback;
    return fallback;
  }
}
