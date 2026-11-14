namespace Platform.Agent;

public static class AgentMemoryServiceExtensions
{
  public static void AddAgentMemoryColumns<TForm>(
    this Columns<AppAgentMemory, AppAgentMemory> columns,
    ILocalizer localizer)
    where TForm : class, IFormService
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    columns.Add(r => r.Id).AsOpenForm<TForm>();
    columns.Add(r => r.Title);
    columns.Add(r => r.Kind)
      .Color(r => ResolveMemoryKindColor(r.Kind));
    columns.Add(r => r.Module);
    columns.Add(r => r.Priority).Width(90);
    columns.Add(r => r.IsPinned).Label(localizer["Pinned"]).AsCheckbox();
    columns.Add(r => r.IsActive).AsCheckbox();
    columns.Add(r => r.UpdatedAt);
  }

  public static void AddAgentMemoryQuery(
    this GridQuery<AppAgentMemory, AppAgentMemory> query,
    ILocalizer localizer)
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    query.SortByDescending(r => r.IsPinned).ThenByDescending(r => r.Priority).ThenBy(r => r.Id);
    query.RecordLabel(e => e.Title);
    query.SearchBy(r => r.Id, r => r.Title, r => r.Kind, r => r.Module, r => r.Content);
    query.NamedFilter("Kind", localizer["Kind"])
      .All(localizer["All"]).Default()
      .Item("Rule", localizer["Rules"], r => r.Kind == AgentMemoryKinds.Rule).Color(UiColor.Rose)
      .Item("Seed", localizer["Seed"], r => r.Kind == AgentMemoryKinds.Seed).Color(UiColor.Blue)
      .Item("Fact", localizer["Facts"], r => r.Kind == AgentMemoryKinds.Fact).Color(UiColor.Emerald)
      .Item("Preference", localizer["Preferences"], r => r.Kind == AgentMemoryKinds.Preference).Color(UiColor.Violet);
    query.NamedFilter("State", localizer["Status"])
      .All(localizer["All"]).Default()
      .Item("Active", localizer["Active"], r => r.IsActive).Color(UiColor.Emerald)
      .Item("Archived", localizer["Disabled"], r => !r.IsActive).Color(UiColor.Slate);
  }

  public static void AddAgentMemoryActions(
    this GridActions<AppAgentMemory> actions,
    TimeProvider time,
    ILocalizer localizer)
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    actions.Add("TogglePinned")
      .AllowEdit()
      .Label(localizer["Toggle pinned"])
      .Icon(UiIcon.Checkmark)
      .Color(UiColor.Amber)
      .Do(ctx =>
      {
        foreach (var rec in ctx.Records)
        {
          rec.IsPinned = !rec.IsPinned;
          rec.UpdatedAt = time.UtcNow;
        }
      });

    actions.Add("ToggleActive")
      .AllowEdit()
      .Label(localizer["Toggle active"])
      .Icon(UiIcon.Edit)
      .Do(ctx =>
      {
        foreach (var rec in ctx.Records)
        {
          rec.IsActive = !rec.IsActive;
          rec.UpdatedAt = time.UtcNow;
        }
      });
  }

  public static void AddAgentMemoryElements(
    this FormElements<AppAgentMemory> elements,
    ILocalizer localizer)
  {
    AgentProviderServiceExtensions.EnsureResourcesRegistered();
    elements.Add(e => e.Title).Focus().Span(6);
    elements.Add(e => e.Kind).Span(3);
    elements.Add(e => e.Module).Span(3);
    elements.Add(e => e.Priority).Span(4);
    elements.Add(e => e.IsPinned).Label(localizer["Pinned"]).AsCheckbox().Span(4);
    elements.Add(e => e.IsActive).AsCheckbox().Span(4);
    elements.Add(e => e.Content).AsTextarea().Span(12);
  }

  public static void AddAgentMemoryHooks(
    this FormHooks<AppAgentMemory> hooks,
    TimeProvider time)
  {
    hooks.AfterLoad((c, _) =>
    {
      if (c.IsNew)
      {
        c.Entity.Kind = AgentMemoryKinds.Fact;
        c.Entity.Priority = 100;
        c.Entity.IsActive = true;
      }

      return Task.CompletedTask;
    });

    hooks.BeforeSave((c, _) =>
    {
      CodeRows.EnsurePascalCaseHandle(c.Entity.Kind, nameof(AppAgentMemory));
      c.Entity.UpdatedAt = time.UtcNow;
      return Task.CompletedTask;
    });
  }

  private static string ResolveMemoryKindColor(string kind) =>
    kind switch
    {
      AgentMemoryKinds.Rule => UiColor.Rose,
      AgentMemoryKinds.Seed => UiColor.Blue,
      AgentMemoryKinds.Preference => UiColor.Violet,
      _ => UiColor.Emerald
    };
}
