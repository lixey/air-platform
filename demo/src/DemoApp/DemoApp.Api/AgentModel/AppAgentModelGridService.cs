namespace DemoApp.AgentModel;

public sealed class AppAgentModelGridService(
  AppDbContext db,
  AppService? app = null,
  IAgentModelSynchronizer? synchronizer = null)
  : AppGridService<AppAgentModel>(db, app)
{
  public override Feature Feature => App.AgentModel;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<AppAgentModelFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.ModelId).Label("Model ID");
    Columns.Add(r => r.ProviderHandle)
      .Label("Provider")
      .Color(r => r.ProviderHandle == "Agy" ? UiColor.Blue : r.ProviderHandle == "Claude" ? UiColor.Amber : r.ProviderHandle == "Codex" ? UiColor.Emerald : UiColor.Violet);
    Columns.Add(r => r.ProviderName).Label("Provider name");
    Columns.Add(r => r.SortOrder).Label("Sort Order").Width(90);
    Columns.Add(r => r.IsDefault).Label("Default").AsCheckbox();
    Columns.Add(r => r.IsForPhotos).Label("For photos").AsCheckbox();
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery()
  {
    if (!ParentId.HasValue)
    {
      Query.Where(r => db.AppAgentProvider.Any(p => p.Handle == r.ProviderHandle && p.IsActive));
    }

    Query.SortBy(r => r.SortOrder).ThenBy(r => r.ProviderHandle).ThenBy(r => r.Name);
    Query.RecordLabel(e => e.Name);
    Query.SearchBy(r => r.Id, r => r.Name, r => r.ModelId, r => r.ProviderHandle, r => r.ProviderName);
    Query.NamedFilter("State", L["Status"])
      .All(L["All"]).Default()
      .Item("Active", L["Active"], r => r.IsActive).Color(UiColor.Emerald)
      .Item("Archived", L["Disabled"], r => !r.IsActive).Color(UiColor.Slate);
  }

  protected override void SetActions()
  {
    Actions.Add("RefreshModels")
      .AllowEdit()
      .Label("Refresh models")
      .Icon(UiIcon.Refresh)
      .Color(UiColor.Blue)
      .WithoutSelection()
      .WithoutTransaction()
      .Do(async (_, ct) =>
      {
        var parentProviderHandle = ParentRecord<AppAgentProvider>()?.Handle;
        if (synchronizer is not null)
        {
          await synchronizer.SyncFromProvidersAsync(db, ct, parentProviderHandle);
        }
        else
        {
          await AgentModelCatalog.EnsureSeededAsync(db, ct);
        }
      });

    Actions.Add("SetAsDefault")
      .AllowEdit()
      .Label("Set as default")
      .Icon(UiIcon.Checkmark)
      .Color(UiColor.Emerald)
      .Single()
      .Do(async (ctx, rec, ct) =>
      {
        if (!await AgentModelCatalog.SetDefaultModelAsync(db, rec.Id, ct))
        {
          ctx.Refuse(L["MutationRefused"], rec.Id);
        }
      });

    Actions.Add("SetForPhotos")
      .AllowEdit()
      .Label("Set for photos")
      .Icon(UiIcon.Photo)
      .Color(UiColor.Violet)
      .Single()
      .Do(async (ctx, rec, ct) =>
      {
        if (!await AgentModelCatalog.SetPhotoModelAsync(db, rec.Id, ct))
        {
          ctx.Refuse(L["MutationRefused"], rec.Id);
        }
      });

    Actions.Add("ToggleActive")
      .AllowEdit()
      .Label("Toggle active")
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
}
