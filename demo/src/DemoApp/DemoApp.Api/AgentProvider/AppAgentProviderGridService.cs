namespace DemoApp.AgentProvider;

public sealed class AppAgentProviderGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAgentProvider>(db, app)
{
  public override Feature Feature => App.AgentProvider;

  protected override void SetColumns(Columns<AppAgentProvider> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppAgentProviderFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Handle)
      .Label("Provider Handle")
      .Color(r => r.Handle == "Agy" ? UiColor.Blue : r.Handle == "Claude" ? UiColor.Amber : r.Handle == "Codex" ? UiColor.Emerald : UiColor.Violet);
    columns.Add(r => r.SortOrder).Label("Sort Order").Width(90);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query)
  {
    query.SortBy(r => r.SortOrder).ThenBy(r => r.Handle);
    query.RecordLabel(e => e.Name);
    query.SearchBy(r => r.Id, r => r.Name, r => r.Handle);
    query.NamedFilter("state", L["Status"])
      .All(L["All"]).Badge().Default()
      .Item("active", L["Active"], r => r.IsActive).Color(UiColor.Emerald).Badge()
      .Item("archived", L["Disabled"], r => !r.IsActive).Color(UiColor.Slate).Badge();
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(
      ToggleActive,
      b => b.Label(L["Toggle active"]).Icon(UiIcon.Edit),
      needsSelection: true);
  }

  public async Task<GridActionDto> ToggleActive(GridRequest request, CancellationToken ct = default)
  {
    var res = await ExecuteBatchAsync(request, async (context, token) =>
    {
      var activeIds = (await db.AppAgentProvider
          .AsNoTracking()
          .Where(p => p.IsActive)
          .Select(p => p.Id)
          .ToListAsync(token))
        .ToHashSet();

      foreach (var rec in context.Records)
      {
        if (rec.IsActive && activeIds.Count <= 1 && activeIds.Contains(rec.Id))
        {
          continue;
        }

        rec.IsActive = !rec.IsActive;
        if (rec.IsActive)
        {
          activeIds.Add(rec.Id);
        }
        else
        {
          activeIds.Remove(rec.Id);
        }
      }
    }, ct);

    if (res.Outcome == MutationOutcome.Committed)
    {
      await AgentModelCatalog.SyncProviderModelsStateAsync(db, ct);
    }

    return res;
  }
}
