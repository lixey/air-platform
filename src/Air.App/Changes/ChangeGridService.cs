namespace Platform.App.Changes;

/// <summary>
/// The change journal (AppPlatformChange) of the tenant: a grid an application routes with a sealed
/// service of its own and a read-only feature.
/// </summary>
public abstract class ChangeGridService(PlatformDbContext db, IServiceProvider? services = null, PlatformAppService? app = null) : GridService<AppPlatformChange>(db, app)
{
  protected override void SetColumns(Columns<AppPlatformChange> columns)
  {
    columns.Add(r => r.At).AsDatetime();
    columns.Add(r => r.UserId);
    columns.Add(r => r.Operation);
    columns.Add(r => r.Table);
    columns.Add(r => r.RowId);
    columns.Add(r => r.Before);
    columns.Add(r => r.After);
    columns.Add(r => r.CorrelationId);
  }

  protected override void SetQuery(Query query)
  {
    query.SortByDescending(r => r.At).ThenByDescending(r => r.Id);
    query.SearchBy(r => r.Table, r => r.Operation, r => r.CorrelationId);
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(Restore, b => b.Label(L["Restore"]).Icon(UiIcon.Reload), L["RestoreConfirm"]).AllowReadOnly();
  }

  public virtual async Task<GridActionDto> Restore(GridRequest request, CancellationToken cancellationToken)
  {
    if (User != null && User.IsAuthenticated &&
        !User.Can(Feature.Action(nameof(Restore))) &&
        !User.Can(Feature.Edit) &&
        !User.Can(Feature.All) &&
        !User.Can("*"))
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

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

            if (((System.Reflection.MemberInfo?)prop.Metadata.PropertyInfo ?? prop.Metadata.FieldInfo) is { } member &&
                Attribute.IsDefined(member, typeof(KeepsVersionAttribute), inherit: true))
            {
              return new GridActionDto
              {
                Outcome = MutationOutcome.Refused,
                Warnings = [new MessageDto { Detail = L["ChangeRestoreKeepsVersion"], Code = "change.restore.keepsVersion", Type = MessageType.Error }]
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
                  Warnings = [new MessageDto { Detail = L["ChangeRestoreStale", kvp.Key], Code = "change.restore.stale", Type = MessageType.Error }]
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
