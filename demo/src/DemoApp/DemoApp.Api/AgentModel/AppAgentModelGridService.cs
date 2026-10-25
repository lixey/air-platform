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

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(
      RefreshModels,
      b => b.Label(L["Refresh models"]).Icon(UiIcon.Reload).Color(UiColor.Blue),
      needsSelection: false);
    nav.AddAction(
      SetAsDefault,
      b => b.Label(L["Set as default"]).Icon(UiIcon.Checkmark).Color(UiColor.Emerald),
      needsSelection: true);
    nav.AddAction(
      SetForPhotos,
      b => b.Label(L["Set for photos"]).Icon(UiIcon.Photo).Color(UiColor.Violet),
      needsSelection: true);
    nav.AddAction(
      ToggleActive,
      b => b.Label(L["Toggle active"]).Icon(UiIcon.Edit),
      needsSelection: true);
  }

  public async Task<GridActionDto> RefreshModels(GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    var parentProviderHandle = ParentRecord<AppAgentProvider>()?.Handle;

    if (synchronizer is not null)
    {
      await synchronizer.SyncFromProvidersAsync(db, ct, parentProviderHandle);
    }
    else
    {
      await AgentModelCatalog.EnsureSeededAsync(db, ct);
    }

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  public async Task<GridActionDto> SetAsDefault(GridRequest request, CancellationToken ct = default)
  {
    var targetId = request.SelectedIds?.FirstOrDefault() ?? request.Id;
    if (!targetId.HasValue)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var ok = await AgentModelCatalog.SetDefaultModelAsync(db, targetId.Value, ct);
    return new GridActionDto { Outcome = ok ? MutationOutcome.Committed : MutationOutcome.Refused };
  }

  public async Task<GridActionDto> SetForPhotos(GridRequest request, CancellationToken ct = default)
  {
    var targetId = request.SelectedIds?.FirstOrDefault() ?? request.Id;
    if (!targetId.HasValue)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var ok = await AgentModelCatalog.SetPhotoModelAsync(db, targetId.Value, ct);
    return new GridActionDto { Outcome = ok ? MutationOutcome.Committed : MutationOutcome.Refused };
  }

  public Task<GridActionDto> ToggleActive(GridRequest request, CancellationToken ct = default) =>
    ExecuteBatchAsync(request, async (context, token) =>
    {
      var activeProviderHandles = await AgentModelCatalog.GetActiveProviderHandlesAsync(db, token);

      foreach (var rec in context.Records)
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
    }, ct);
}
