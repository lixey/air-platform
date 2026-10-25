namespace DemoApp.AgentModel;

public sealed class AppAgentModelFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentModel>(db, app)
{
  public override Feature Feature => App.AgentModel;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Popover(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus().Span(6);
    elements.Add(e => e.ModelId).Label("Model ID").Span(6);
    elements.Add(e => e.ProviderHandle).Label("Provider").Span(6);
    elements.Add(e => e.ProviderName).Label("Provider name").Span(6);
    elements.Add(e => e.SortOrder).Label("Sort Order").Span(3);
    elements.Add(e => e.IsDefault).Label("Default").AsCheckbox().Span(3);
    elements.Add(e => e.IsForPhotos).Label("For photos").AsCheckbox().Span(3);
    elements.Add(e => e.IsActive).AsCheckbox().Span(3);
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad(async (c, ct) =>
    {
      if (c.IsNew && string.IsNullOrEmpty(c.Entity.ProviderHandle))
      {
        var defProvider = await db.AppAgentProvider
          .AsNoTracking()
          .Where(p => p.IsActive)
          .OrderBy(p => p.SortOrder)
          .FirstOrDefaultAsync(ct);
        c.Entity.ProviderHandle = defProvider?.Handle ?? "Agy";
        c.Entity.ProviderName = defProvider?.Name ?? "Antigravity CLI (agy)";
        c.Entity.SortOrder = 50;
        c.Entity.IsActive = true;
      }
      else if (c.IsNew && c.Entity.SortOrder == 0)
      {
        c.Entity.SortOrder = 50;
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
          c.Refuse(L["Cannot activate a model of a disabled provider."]);
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
