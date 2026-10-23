using Air.App.Contracts;
using Air.Data;

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
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query)
  {
    query.SortBy(r => r.SortOrder).ThenBy(r => r.ProviderHandle).ThenBy(r => r.Name);
    query.RecordLabel(e => e.Name);
    query.SearchBy(r => r.Id, r => r.Name, r => r.ModelId, r => r.ProviderHandle, r => r.ProviderName);
    query.NamedFilter("state", L["Status"])
      .Item("active", L["Active"], r => r.IsActive).Color(UiColor.Emerald).Badge().Default()
      .Item("archived", L["Disabled"], r => !r.IsActive).Color(UiColor.Slate).Badge()
      .All(L["All"]).Badge();
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
      ToggleActive,
      b => b.Label(L["Toggle active"]).Icon(UiIcon.Edit),
      needsSelection: true);
  }

  public async Task<GridActionDto> RefreshModels(GridRequest request, CancellationToken ct = default)
  {
    if (synchronizer is not null)
    {
      await synchronizer.SyncFromProvidersAsync(db, ct);
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

    await AgentModelCatalog.SetDefaultModelAsync(db, targetId.Value, ct);
    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  public Task<GridActionDto> ToggleActive(GridRequest request, CancellationToken ct = default) =>
    ExecuteBatchAsync(request, (context, _) =>
    {
      foreach (var rec in context.Records)
      {
        if (rec.IsDefault && rec.IsActive)
        {
          continue;
        }

        rec.IsActive = !rec.IsActive;
      }

      return Task.CompletedTask;
    }, ct);
}
