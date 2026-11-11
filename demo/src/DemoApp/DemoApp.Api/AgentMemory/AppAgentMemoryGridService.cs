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
      .All(L["All"]).Default()
      .Item("rule", L["Rules"], r => r.Kind == AgentMemoryKinds.Rule).Color(UiColor.Rose)
      .Item("seed", L["Seed"], r => r.Kind == AgentMemoryKinds.Seed).Color(UiColor.Blue)
      .Item("fact", L["Facts"], r => r.Kind == AgentMemoryKinds.Fact).Color(UiColor.Emerald)
      .Item("preference", L["Preferences"], r => r.Kind == AgentMemoryKinds.Preference).Color(UiColor.Violet);
    query.NamedFilter("state", L["Status"])
      .All(L["All"]).Default()
      .Item("active", L["Active"], r => r.IsActive).Color(UiColor.Emerald)
      .Item("archived", L["Disabled"], r => !r.IsActive).Color(UiColor.Slate);
  }

  protected override void SetActions(Actions actions)
  {
    actions.Add("TogglePinned")
      .AllowEdit()
      .Label("Toggle pinned")
      .Icon(UiIcon.Checkmark)
      .Color(UiColor.Amber)
      .Do(ctx =>
      {
        foreach (var rec in ctx.Records)
        {
          rec.IsPinned = !rec.IsPinned;
          rec.UpdatedAt = Time.UtcNow;
        }
      });

    actions.Add("ToggleActive")
      .AllowEdit()
      .Label("Toggle active")
      .Icon(UiIcon.Edit)
      .Do(ctx =>
      {
        foreach (var rec in ctx.Records)
        {
          rec.IsActive = !rec.IsActive;
          rec.UpdatedAt = Time.UtcNow;
        }
      });
  }
}
