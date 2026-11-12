namespace DemoApp.AgentModel;

public sealed class AppAgentModelGridService(
  AppDbContext db,
  AppService? app = null,
  IAgentModelSynchronizer? synchronizer = null)
  : AppGridService<AppAgentModel>(db, app)
{
  public override Feature Feature => App.AgentModel;

  protected override void SetColumns(Columns<AppAgentModel> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppAgentModelFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.ModelId).Label("Model ID");
    columns.Add(r => r.ProviderHandle)
      .Label("Provider")
      .Color(r => r.ProviderHandle == "Agy" ? UiColor.Blue : r.ProviderHandle == "Claude" ? UiColor.Amber : r.ProviderHandle == "Codex" ? UiColor.Emerald : UiColor.Violet);
    columns.Add(r => r.ProviderName).Label("Provider name");
    columns.Add(r => r.SortOrder).Label("Sort Order").Width(90);
    columns.Add(r => r.IsDefault).Label("Default").AsCheckbox();
    columns.Add(r => r.IsForPhotos).Label("For photos").AsCheckbox();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query)
  {
    if (!ParentId.HasValue)
    {
      query.Where(r => db.AppAgentProvider.Any(p => p.Handle == r.ProviderHandle && p.IsActive));
    }

    query.SortBy(r => r.SortOrder).ThenBy(r => r.ProviderHandle).ThenBy(r => r.Name);
    query.RecordLabel(e => e.Name);
    query.SearchBy(r => r.Id, r => r.Name, r => r.ModelId, r => r.ProviderHandle, r => r.ProviderName);
    query.NamedFilter("state", L["Status"])
      .All(L["All"]).Default()
      .Item("active", L["Active"], r => r.IsActive).Color(UiColor.Emerald)
      .Item("archived", L["Disabled"], r => !r.IsActive).Color(UiColor.Slate);
  }

  protected override void SetActions(Actions actions)
  {
    actions.Add("RefreshModels")
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

    actions.Add("SetAsDefault")
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

    actions.Add("SetForPhotos")
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

    actions.Add("ToggleActive")
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
