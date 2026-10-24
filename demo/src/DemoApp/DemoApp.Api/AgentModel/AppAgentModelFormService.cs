using Air.Data;

namespace DemoApp.AgentModel;

public sealed class AppAgentModelFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentModel>(db, app)
{
  public override Feature Feature => App.AgentModel;

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
    hooks.AfterLoad((c, _) =>
    {
      if (c.IsNew)
      {
        c.Entity.ProviderHandle = "Agy";
        c.Entity.ProviderName = "Antigravity CLI (agy)";
        c.Entity.SortOrder = 50;
        c.Entity.IsActive = true;
      }

      return Task.CompletedTask;
    });

    hooks.BeforeSave((c, _) =>
    {
      CodeRows.EnsurePascalCaseHandle(c.Entity.ProviderHandle, nameof(AppAgentModel));
      if (c.Entity.IsDefault || c.Entity.IsForPhotos)
      {
        c.Entity.IsActive = true;
      }

      return Task.CompletedTask;
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
