namespace Platform.App.Changes;

/// <summary>
/// The change journal (AppPlatformChange) of the tenant: a grid an application routes with a sealed
/// service of its own and a read-only feature.
/// </summary>
public abstract partial class ChangeGridService(PlatformDbContext db, IServiceProvider? services = null, PlatformAppService? app = null) : GridService<AppPlatformChange>(db, app)
{
  protected override bool IsReadOnly => true;

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
      .Item("deleted", "Deleted", r => r.Operation == ChangeOperations.Deleted).Color(UiColor.Red)
      .Item("restored", "Restored", r => r.Operation == ChangeOperations.Restored).Color(UiColor.Indigo);

    query.SortByDescending(r => r.At).ThenByDescending(r => r.Id);
    query.SearchBy(r => r.Table, r => r.Operation, r => r.Before, r => r.After, r => r.CorrelationId);
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

      if (dict.TryGetValue("before", out var beforeObj) && beforeObj is string beforeStr)
      {
        dict["before"] = UnescapeJsonText(beforeStr);
      }

      if (dict.TryGetValue("after", out var afterObj) && afterObj is string afterStr)
      {
        dict["after"] = UnescapeJsonText(afterStr);
      }
    }

    return shaped;
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    if (CanRestoreInCurrentScope())
    {
      nav.AddAction(Restore, b => b.Label(L["Restore"]).Icon(UiIcon.Reload), L["RestoreConfirm"]).AllowReadOnly();
      nav.AvailableWhen(
        Restore,
        r => (r.Operation == ChangeOperations.Modified || r.Operation == ChangeOperations.Deleted) && r.Before != null && r.Before != "",
        "ChangeRestoreNotRestorable");
    }
  }

  [RequireAction(EndpointAction.Restore)]
  public virtual async Task<GridActionDto> Restore(GridRequest request, CancellationToken cancellationToken)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;

    var ids = request.SelectedIds ?? [];
    if (ids.Count == 0 && request.Id.HasValue)
    {
      ids = [request.Id.Value];
    }

    if (ids.Count == 0)
    {
      return new GridActionDto { Outcome = MutationOutcome.Committed };
    }

    var changes = await RestrictedQuery()
      .Where(c => ids.Contains(c.Id))
      .OrderByDescending(c => c.Id)
      .ToListAsync(cancellationToken);

    if (changes.Count != ids.Distinct().Count())
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["ChangeRestoreUnavailable"], Code = "change.restore.unavailable", Type = MessageType.Error }]
      };
    }

    foreach (var change in changes)
    {
      if (!CanRestoreTable(change.Table))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }
    }

    var modifiedContexts = new HashSet<PlatformDbContext>();

    foreach (var change in changes)
    {
      if (change.Operation is not ChangeOperations.Modified and not ChangeOperations.Deleted || string.IsNullOrEmpty(change.Before))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["ChangeRestoreNotRestorable"], Code = "change.restore.notRestorable", Type = MessageType.Error }]
        };
      }

      var targetDb = GetDbContextForTable(change.Table);
      var entityType = targetDb.Model.GetEntityTypes()
        .FirstOrDefault(e => e.ClrType.Name == change.Table || e.GetTableName() == change.Table)?.ClrType;
      if (entityType == null || typeof(BaseAppFile).IsAssignableFrom(entityType))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["ChangeRestoreNotRestorable"], Code = "change.restore.notRestorable", Type = MessageType.Error }]
        };
      }

      if (change.Operation == ChangeOperations.Modified)
      {
        var entity = await targetDb.FindAsync(entityType, [change.RowId], cancellationToken);
        if (entity == null)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Warnings = [new MessageDto { Detail = L["ChangeRestoreMissingRow"], Code = "change.restore.missingRow", Type = MessageType.Error }]
          };
        }

        var beforeDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(change.Before);
        var afterDict = !string.IsNullOrEmpty(change.After)
          ? JsonSerializer.Deserialize<Dictionary<string, object?>>(change.After)
          : null;
        if (beforeDict != null)
        {
          var entry = targetDb.Entry(entity);
          foreach (var kvp in beforeDict)
          {
            if (kvp.Key is "Ver" or "UpdatedAt" or "CreatedAt")
            {
              continue;
            }

            var prop = entry.Metadata.FindProperty(kvp.Key) is { } meta ? entry.Property(meta.Name) : null;
            if (prop == null || prop.Metadata.IsPrimaryKey())
            {
              continue;
            }

            if (((MemberInfo?)prop.Metadata.PropertyInfo ?? prop.Metadata.FieldInfo) is { } member &&
                Attribute.IsDefined(member, typeof(KeepsVersionAttribute), inherit: true))
            {
              return new GridActionDto
              {
                Outcome = MutationOutcome.Refused,
                Warnings = [new MessageDto { Detail = L["ChangeRestoreKeepsVersion", change.Table, change.RowId], Code = "change.restore.keepsVersion", Type = MessageType.Error }]
              };
            }

            if (afterDict != null && afterDict.TryGetValue(kvp.Key, out var afterRaw))
            {
              var expectedAfter = DeserializePropertyValue(afterRaw, prop.Metadata.ClrType);
              if (!Equals(prop.CurrentValue, expectedAfter))
              {
                return new GridActionDto
                {
                  Outcome = MutationOutcome.Conflict,
                  Warnings = [new MessageDto { Detail = L["ChangeRestoreStale", change.Table, change.RowId], Code = "change.restore.stale", Type = MessageType.Error }]
                };
              }
            }

            prop.CurrentValue = DeserializePropertyValue(kvp.Value, prop.Metadata.ClrType);
          }

          if (entity is BaseAppUser restoredUser)
          {
            restoredUser.IsActive = false;
            restoredUser.IsRoot = false;
          }

          targetDb.MarkRestored(entity);
          modifiedContexts.Add(targetDb);
        }
      }
      else if (change.Operation == ChangeOperations.Deleted)
      {
        var existing = await targetDb.FindAsync(entityType, [change.RowId], cancellationToken);
        if (existing != null)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Warnings = [new MessageDto { Detail = L["ChangeRestoreExists"], Code = "change.restore.exists", Type = MessageType.Error }]
          };
        }

        var beforeDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(change.Before);
        if (beforeDict != null)
        {
          var entity = Activator.CreateInstance(entityType);
          if (entity != null)
          {
            var entry = targetDb.Entry(entity);
            foreach (var kvp in beforeDict)
            {
              var prop = entry.Metadata.FindProperty(kvp.Key) is { } meta ? entry.Property(meta.Name) : null;
              prop?.CurrentValue = DeserializePropertyValue(kvp.Value, prop.Metadata.ClrType);
            }

            if (entity is BaseAppUser restoredUser)
            {
              restoredUser.IsActive = false;
              restoredUser.IsRoot = false;
            }

            entry.State = EntityState.Added;
            targetDb.MarkRestored(entity);
            modifiedContexts.Add(targetDb);
          }
        }
      }
    }

    foreach (var ctx in modifiedContexts)
    {
      await ctx.SaveChangesAsync(cancellationToken);
    }

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  private bool CanRestoreInCurrentScope()
  {
    if (User is not { IsAuthenticated: true })
    {
      return true;
    }

    if (User.Can(Feature.Restore) || User.Can(Feature.All) || User.Can("*"))
    {
      return true;
    }

    if (!string.IsNullOrEmpty(ParentService) && ModuleCatalog.Instance.FindFeature(ParentService) is { HasHistory: true } parentFeat)
    {
      return User.Can(parentFeat.Restore) || User.Can(parentFeat.All);
    }

    return false;
  }

  private bool CanRestoreTable(string table)
  {
    if (CanRestoreInCurrentScope())
    {
      return true;
    }

    if (User is not { IsAuthenticated: true })
    {
      return true;
    }

    var targetDb = GetDbContextForTable(table);
    var entityType = targetDb.Model.GetEntityTypes()
      .FirstOrDefault(e => e.ClrType.Name == table || e.GetTableName() == table)?.ClrType;
    var tableFeat = ModuleCatalog.Instance.FindFeatureForEntity(entityType, table);
    return tableFeat is { HasHistory: true } && (User.Can(tableFeat.Restore) || User.Can(tableFeat.All));
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
    if (userIds.Count == 0)
    {
      return result;
    }

    var userClrType = db.Model.GetEntityTypes()
      .Select(e => e.ClrType)
      .FirstOrDefault(t => typeof(BaseAppUser).IsAssignableFrom(t) && !t.IsAbstract);
    if (userClrType == null)
    {
      return result;
    }

    try
    {
      var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(userClrType);
      if (setMethod.Invoke(db, null) is IQueryable<BaseAppUser> userQuery)
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
    }
    catch { }

    return result;
  }

  private static object? DeserializePropertyValue(object? raw, Type clrType)
  {
    var targetType = Nullable.GetUnderlyingType(clrType) ?? clrType;
    if (raw is JsonElement je)
    {
      return JsonSerializer.Deserialize(je.GetRawText(), clrType);
    }

    return raw != null ? Convert.ChangeType(raw, targetType) : null;
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
