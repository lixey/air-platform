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
    query.NamedFilter("Kind", L["Kind"])
      .All(L["All"]).Default()
      .Item("Rule", L["Rules"], r => r.Kind == AgentMemoryKinds.Rule).Color(UiColor.Rose)
      .Item("Seed", L["Seed"], r => r.Kind == AgentMemoryKinds.Seed).Color(UiColor.Blue)
      .Item("Fact", L["Facts"], r => r.Kind == AgentMemoryKinds.Fact).Color(UiColor.Emerald)
      .Item("Preference", L["Preferences"], r => r.Kind == AgentMemoryKinds.Preference).Color(UiColor.Violet);
    query.NamedFilter("State", L["Status"])
      .All(L["All"]).Default()
      .Item("Active", L["Active"], r => r.IsActive).Color(UiColor.Emerald)
      .Item("Archived", L["Disabled"], r => !r.IsActive).Color(UiColor.Slate);
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
