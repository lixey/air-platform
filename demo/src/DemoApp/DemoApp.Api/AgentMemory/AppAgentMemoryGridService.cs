namespace DemoApp.AgentMemory;

public sealed class AppAgentMemoryGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAgentMemory>(db, app)
{
  public override Feature Feature => App.AgentMemory;

  protected override void SetColumns(Columns<AppAgentMemory> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppAgentMemoryFormService>();
    columns.Add(r => r.Title);
    columns.Add(r => r.Kind)
      .Color(r => r.Kind == AgentMemoryKinds.Rule ? UiColor.Rose : r.Kind == AgentMemoryKinds.Seed ? UiColor.Blue : r.Kind == AgentMemoryKinds.Preference ? UiColor.Violet : UiColor.Emerald);
    columns.Add(r => r.Module);
    columns.Add(r => r.Priority).Width(90);
    columns.Add(r => r.IsPinned).Label("Pinned").AsCheckbox();
    columns.Add(r => r.IsActive).AsCheckbox();
    columns.Add(r => r.UpdatedAt);
  }

  protected override void SetQuery(Query query)
  {
    query.SortByDescending(r => r.IsPinned).ThenByDescending(r => r.Priority).ThenBy(r => r.Id);
    query.RecordLabel(e => e.Title);
    query.SearchBy(r => r.Id, r => r.Title, r => r.Kind, r => r.Module, r => r.Content);
    query.NamedFilter("kind", L["Kind"])
      .All(L["All"]).Badge().Default()
      .Item("rule", L["Rules"], r => r.Kind == AgentMemoryKinds.Rule).Color(UiColor.Rose).Badge()
      .Item("seed", L["Seed"], r => r.Kind == AgentMemoryKinds.Seed).Color(UiColor.Blue).Badge()
      .Item("fact", L["Facts"], r => r.Kind == AgentMemoryKinds.Fact).Color(UiColor.Emerald).Badge()
      .Item("preference", L["Preferences"], r => r.Kind == AgentMemoryKinds.Preference).Color(UiColor.Violet).Badge();
    query.NamedFilter("state", L["Status"])
      .All(L["All"]).Badge().Default()
      .Item("active", L["Active"], r => r.IsActive).Color(UiColor.Emerald).Badge()
      .Item("archived", L["Disabled"], r => !r.IsActive).Color(UiColor.Slate).Badge();
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(
      TogglePinned,
      b => b.Label(L["Toggle pinned"]).Icon(UiIcon.Checkmark).Color(UiColor.Amber),
      needsSelection: true);
    nav.AddAction(
      ToggleActive,
      b => b.Label(L["Toggle active"]).Icon(UiIcon.Edit),
      needsSelection: true);
  }

  public Task<GridActionDto> TogglePinned(GridRequest request, CancellationToken ct = default) =>
    ExecuteBatchAsync(request, (context, _) =>
    {
      foreach (var rec in context.Records)
      {
        rec.IsPinned = !rec.IsPinned;
        rec.UpdatedAt = DateTimeOffset.UtcNow;
      }

      return Task.CompletedTask;
    }, ct);

  public Task<GridActionDto> ToggleActive(GridRequest request, CancellationToken ct = default) =>
    ExecuteBatchAsync(request, (context, _) =>
    {
      foreach (var rec in context.Records)
      {
        rec.IsActive = !rec.IsActive;
        rec.UpdatedAt = DateTimeOffset.UtcNow;
      }

      return Task.CompletedTask;
    }, ct);
}
