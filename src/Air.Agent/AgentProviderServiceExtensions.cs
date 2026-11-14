using Platform.Agent.Resources;

namespace Platform.Agent;

public static class AgentProviderServiceExtensions
{
  static AgentProviderServiceExtensions() =>
    Localizer.Instance.Register(typeof(AgentResource));

  internal static void EnsureResourcesRegistered() =>
    Localizer.Instance.Register(typeof(AgentResource));

  public static void AddAgentProviderColumns<TForm>(
    this Columns<AppAgentProvider, AppAgentProvider> columns,
    ILocalizer localizer)
    where TForm : class, IFormService
  {
    EnsureResourcesRegistered();
    columns.Add(r => r.Id).AsOpenForm<TForm>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Handle)
      .Label(localizer["Provider Handle"])
      .Color(r => ResolveProviderColor(r.Handle));
    columns.Add(r => r.SortOrder).Label(localizer["Sort Order"]).Width(90);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  public static void AddAgentProviderQuery(
    this GridQuery<AppAgentProvider, AppAgentProvider> query,
    ILocalizer localizer)
  {
    EnsureResourcesRegistered();
    query.SortBy(r => r.SortOrder).ThenBy(r => r.Handle);
    query.RecordLabel(e => e.Name);
    query.SearchBy(r => r.Id, r => r.Name, r => r.Handle);
    query.NamedFilter("State", localizer["Status"])
      .All(localizer["All"]).Default()
      .Item("Active", localizer["Active"], r => r.IsActive).Color(UiColor.Emerald)
      .Item("Archived", localizer["Disabled"], r => !r.IsActive).Color(UiColor.Slate);
  }

  public static void AddAgentProviderHooks(
    this GridHooks<AppAgentProvider> hooks,
    PlatformDbContext db,
    ILocalizer localizer)
  {
    EnsureResourcesRegistered();
    hooks.BeforeDelete(async (c, ct) =>
    {
      var builtInHandles = AgentModelCatalog.DefaultProviders()
        .Select(p => p.Handle)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      foreach (var rec in c.Records)
      {
        var isBuiltIn = builtInHandles.Contains(rec.Handle);
        var hasModels = await db.AppAgentModel
          .AnyAsync(m => m.ProviderHandle == rec.Handle, ct);

        if (isBuiltIn || hasModels)
        {
          c.Refuse(localizer["Cannot delete a provider that has models or is built-in."], rec.Id);
          return;
        }
      }
    });
  }

  public static void AddAgentProviderActions(
    this GridActions<AppAgentProvider> actions,
    PlatformDbContext db,
    ILocalizer localizer)
  {
    EnsureResourcesRegistered();
    actions.Add("ToggleActive")
      .AllowEdit()
      .Label(localizer["Toggle active"])
      .Icon(UiIcon.Edit)
      .Do(async (ctx, ct) =>
      {
        var activeIds = (await db.AppAgentProvider
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(ct))
          .ToHashSet();

        foreach (var rec in ctx.Records)
        {
          if (rec.IsActive && activeIds.Count <= 1 && activeIds.Contains(rec.Id))
          {
            continue;
          }

          rec.IsActive = !rec.IsActive;
          if (rec.IsActive)
          {
            activeIds.Add(rec.Id);
          }
          else
          {
            activeIds.Remove(rec.Id);
          }
        }

        await ctx.FlushAsync(ct);
        await AgentModelCatalog.SyncProviderModelsStateAsync(db, ct);
      });
  }

  public static void AddAgentProviderElements<TModelGrid>(
    this FormElements<AppAgentProvider> elements,
    bool isNew,
    ILocalizer localizer)
    where TModelGrid : class, IGridService
  {
    EnsureResourcesRegistered();
    elements.Add(e => e.Name).Focus().Span(6);
    var handleEl = elements.Add(e => e.Handle).Label(localizer["Provider Handle"]).Span(3);
    if (!isNew)
    {
      handleEl.Disabled();
    }

    elements.Add(e => e.SortOrder).Label(localizer["Sort Order"]).Span(3);
    elements.Add(e => e.IsActive).AsCheckbox().Span(3);

    elements.AddChild<TModelGrid, AppAgentModel>(
        p => r => r.ProviderHandle == p.Handle,
        (r, p) =>
        {
          r.ProviderHandle = p.Handle;
          r.ProviderName = p.Name;
        })
      .Key("models")
      .Label(localizer["AI models"]);
  }

  public static void AddAgentProviderHooks(
    this FormHooks<AppAgentProvider> hooks,
    PlatformDbContext db,
    ILocalizer localizer)
  {
    EnsureResourcesRegistered();
    hooks.AfterLoad((c, _) =>
    {
      if (c.IsNew)
      {
        c.Entity.SortOrder = 50;
        c.Entity.IsActive = true;
      }

      return Task.CompletedTask;
    });

    hooks.BeforeSave(async (c, ct) =>
    {
      CodeRows.EnsurePascalCaseHandle(c.Entity.Handle, nameof(AppAgentProvider));
      if (!c.Entity.IsActive)
      {
        var hasOtherActive = await db.AppAgentProvider
          .AnyAsync(p => p.IsActive && p.Id != c.Entity.Id, ct);
        if (!hasOtherActive)
        {
          c.Refuse(localizer["At least one AI provider must remain active."]);
        }
      }
    });

    hooks.AfterSave(async (c, ct) =>
    {
      var models = await db.AppAgentModel
        .Where(m => m.ProviderHandle == c.Entity.Handle)
        .ToListAsync(ct);

      foreach (var m in models)
      {
        if (m.ProviderName != c.Entity.Name)
        {
          m.ProviderName = c.Entity.Name;
        }
      }

      await AgentModelCatalog.SyncProviderModelsStateAsync(db, ct);
    });
  }

  internal static string ResolveProviderColor(string handle) =>
    handle switch
    {
      "Agy" => UiColor.Blue,
      "Claude" => UiColor.Amber,
      "Codex" => UiColor.Emerald,
      _ => UiColor.Violet
    };
}
