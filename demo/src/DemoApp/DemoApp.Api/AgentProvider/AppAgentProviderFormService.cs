namespace DemoApp.AgentProvider;

public sealed class AppAgentProviderFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentProvider>(db, app)
{
  public override Feature Feature => App.AgentProvider;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus().Span(6);
    var handleEl = elements.Add(e => e.Handle).Label("Provider Handle").Span(3);
    if (!IsNew)
    {
      handleEl.Disabled();
    }

    elements.Add(e => e.SortOrder).Label("Sort Order").Span(3);
    elements.Add(e => e.IsActive).AsCheckbox().Span(3);

    elements.AddChild<AppAgentModelGridService, AppAgentModel>(
        p => r => r.ProviderHandle == p.Handle,
        (r, p) =>
        {
          r.ProviderHandle = p.Handle;
          r.ProviderName = p.Name;
        })
      .Key("models")
      .Label("AI models");
  }

  protected override void SetHooks(Hooks hooks)
  {
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
          c.Refuse(L["At least one AI provider must remain active."]);
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
}
