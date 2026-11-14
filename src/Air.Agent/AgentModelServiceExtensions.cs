namespace Platform.Agent;

public static class AgentModelServiceExtensions
{
  public static void AddAgentModelColumns<TForm>(
    this Columns<AppAgentModel, AppAgentModel> columns,
    ILocalizer localizer)
    where TForm : class, IFormService
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    columns.Add(r => r.Id).AsOpenForm<TForm>();
    columns.Add(r => r.Name);
    columns.Add(r => r.ModelId).Label(localizer["Model ID"]);
    columns.Add(r => r.ProviderHandle)
      .Label(localizer["Provider"])
      .Color(r => AgentProviderServiceExtensions.ResolveProviderColor(r.ProviderHandle));
    columns.Add(r => r.ProviderName).Label(localizer["Provider name"]);
    columns.Add(r => r.SortOrder).Label(localizer["Sort Order"]).Width(90);
    columns.Add(r => r.IsDefault).Label(localizer["Default"]).AsCheckbox();
    columns.Add(r => r.IsForPhotos).Label(localizer["For photos"]).AsCheckbox();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  public static void AddAgentModelQuery(
    this GridQuery<AppAgentModel, AppAgentModel> query,
    PlatformDbContext db,
    long? parentId,
    ILocalizer localizer)
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    if (!parentId.HasValue)
    {
      query.Where(r => db.AppAgentProvider.Any(p => p.Handle == r.ProviderHandle && p.IsActive));
    }

    query.SortBy(r => r.SortOrder).ThenBy(r => r.ProviderHandle).ThenBy(r => r.Name);
    query.RecordLabel(e => e.Name);
    query.SearchBy(r => r.Id, r => r.Name, r => r.ModelId, r => r.ProviderHandle, r => r.ProviderName);
    query.NamedFilter("State", localizer["Status"])
      .All(localizer["All"]).Default()
      .Item("Active", localizer["Active"], r => r.IsActive).Color(UiColor.Emerald)
      .Item("Archived", localizer["Disabled"], r => !r.IsActive).Color(UiColor.Slate);
  }

  public static void AddAgentModelActions(
    this GridActions<AppAgentModel> actions,
    PlatformDbContext db,
    ILocalizer localizer,
    IAgentModelSynchronizer? synchronizer = null,
    Func<string?>? parentProviderHandle = null)
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    actions.Add("RefreshModels")
      .AllowEdit()
      .Label(localizer["Refresh models"])
      .Icon(UiIcon.Refresh)
      .Color(UiColor.Blue)
      .WithoutSelection()
      .WithoutTransaction()
      .Do(async (_, ct) =>
      {
        if (synchronizer is not null)
        {
          await synchronizer.SyncFromProvidersAsync(db, ct, parentProviderHandle?.Invoke());
        }
        else
        {
          await AgentModelCatalog.EnsureSeededAsync(db, ct);
        }
      });

    actions.Add("SetAsDefault")
      .AllowEdit()
      .Label(localizer["Set as default"])
      .Icon(UiIcon.Checkmark)
      .Color(UiColor.Emerald)
      .Single()
      .Do(async (ctx, rec, ct) =>
      {
        if (!await AgentModelCatalog.SetDefaultModelAsync(db, rec.Id, ct))
        {
          ctx.Refuse(localizer["MutationRefused"], rec.Id);
        }
      });

    actions.Add("SetForPhotos")
      .AllowEdit()
      .Label(localizer["Set for photos"])
      .Icon(UiIcon.Photo)
      .Color(UiColor.Violet)
      .Single()
      .Do(async (ctx, rec, ct) =>
      {
        if (!await AgentModelCatalog.SetPhotoModelAsync(db, rec.Id, ct))
        {
          ctx.Refuse(localizer["MutationRefused"], rec.Id);
        }
      });

    actions.Add("ToggleActive")
      .AllowEdit()
      .Label(localizer["Toggle active"])
      .Icon(UiIcon.Edit)
      .Do(async (ctx, ct) =>
      {
        var activeProviderHandles = await AgentModelCatalog.GetActiveProviderHandlesAsync(db, ct);
        foreach (var rec in ctx.Records)
        {
          if ((rec.IsDefault || rec.IsForPhotos) && rec.IsActive)
          {
            continue;
          }

          if (!rec.IsActive && !activeProviderHandles.Contains(rec.ProviderHandle))
          {
            continue;
          }

          rec.IsActive = !rec.IsActive;
        }
      });
  }

  public static void AddAgentModelElements(
    this FormElements<AppAgentModel> elements,
    ILocalizer localizer)
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    elements.Add(e => e.Name).Focus().Span(6);
    elements.Add(e => e.ModelId).Label(localizer["Model ID"]).Span(6);
    elements.Add(e => e.ProviderHandle).Label(localizer["Provider"]).Span(6);
    elements.Add(e => e.ProviderName).Label(localizer["Provider name"]).Span(6);
    elements.Add(e => e.SortOrder).Label(localizer["Sort Order"]).Span(3);
    elements.Add(e => e.IsDefault).Label(localizer["Default"]).AsCheckbox().Span(3);
    elements.Add(e => e.IsForPhotos).Label(localizer["For photos"]).AsCheckbox().Span(3);
    elements.Add(e => e.IsActive).AsCheckbox().Span(3);
  }

  public static void AddAgentModelHooks(
    this FormHooks<AppAgentModel> hooks,
    PlatformDbContext db,
    ILocalizer localizer)
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    hooks.AfterLoad(async (c, ct) =>
    {
      if (c.IsNew)
      {
        var hasActiveProvider = !string.IsNullOrEmpty(c.Entity.ProviderHandle) &&
          await db.AppAgentProvider
            .AsNoTracking()
            .AnyAsync(p => p.Handle == c.Entity.ProviderHandle && p.IsActive, ct);
        if (!hasActiveProvider)
        {
          var defProvider = await db.AppAgentProvider
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .FirstOrDefaultAsync(ct);
          c.Entity.ProviderHandle = defProvider?.Handle ?? "Agy";
          c.Entity.ProviderName = defProvider?.Name ?? "Antigravity CLI (agy)";
          c.Entity.IsActive = true;
        }

        if (c.Entity.SortOrder == 0)
        {
          c.Entity.SortOrder = 50;
        }
      }
    });

    hooks.BeforeSave(async (c, ct) =>
    {
      CodeRows.EnsurePascalCaseHandle(c.Entity.ProviderHandle, nameof(AppAgentModel));
      if (c.Entity.IsDefault || c.Entity.IsForPhotos)
      {
        c.Entity.IsActive = true;
      }

      if (c.Entity.IsActive)
      {
        var providerActive = await db.AppAgentProvider
          .AnyAsync(p => p.Handle == c.Entity.ProviderHandle && p.IsActive, ct);
        if (!providerActive)
        {
          c.Refuse(localizer["Cannot activate a model of a disabled provider."]);
        }
      }
    });

    hooks.AfterSave(async (c, ct) =>
    {
      if (c.Entity.IsDefault)
      {
        await AgentModelCatalog.SetDefaultModelAsync(db, c.Entity.Id, ct);
      }

      if (c.Entity.IsForPhotos)
      {
        await AgentModelCatalog.SetPhotoModelAsync(db, c.Entity.Id, ct);
      }
    });
  }
}
