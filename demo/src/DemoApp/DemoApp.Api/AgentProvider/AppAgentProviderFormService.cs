namespace DemoApp.AgentProvider;

public sealed class AppAgentProviderFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentProvider>(db, app)
{
  public override Feature Feature => App.AgentProvider;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus().Span(6);
    var handleEl = Elements.Add(e => e.Handle).Label("Provider Handle").Span(3);
    if (!IsNew)
    {
      handleEl.Disabled();
    }

    Elements.Add(e => e.SortOrder).Label("Sort Order").Span(3);
    Elements.Add(e => e.IsActive).AsCheckbox().Span(3);

    Elements.AddChild<AppAgentModelGridService, AppAgentModel>(
        p => r => r.ProviderHandle == p.Handle,
        (r, p) =>
        {
          r.ProviderHandle = p.Handle;
          r.ProviderName = p.Name;
        })
      .Key("models")
      .Label("AI models");
  }

  protected override void SetHooks()
  {
    Hooks.AfterLoad((c, _) =>
    {
      if (c.IsNew)
      {
        c.Entity.SortOrder = 50;
        c.Entity.IsActive = true;
      }

      return Task.CompletedTask;
    });

    Hooks.BeforeSave(async (c, ct) =>
    {
      CodeRows.EnsurePascalCaseHandle(c.Entity.Handle, nameof(AppAgentProvider));
      if (!c.Entity.IsActive)
      {
        var hasOtherActive = await db.AppAgentProvider
          .AnyAsync(p => p.IsActive && p.Id != c.Entity.Id, ct);
        if (!hasOtherActive)
        {
          c.Refuse(L["At least one AI provider must remain active."]);
        }
      }
    });

    Hooks.AfterSave(async (c, ct) =>
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
}
