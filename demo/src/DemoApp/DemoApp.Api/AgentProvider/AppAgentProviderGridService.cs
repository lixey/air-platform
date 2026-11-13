namespace DemoApp.AgentProvider;

public sealed class AppAgentProviderGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAgentProvider>(db, app)
{
  public override Feature Feature => App.AgentProvider;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<AppAgentProviderFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Handle)
      .Label("Provider Handle")
      .Color(r => r.Handle == "Agy" ? UiColor.Blue : r.Handle == "Claude" ? UiColor.Amber : r.Handle == "Codex" ? UiColor.Emerald : UiColor.Violet);
    Columns.Add(r => r.SortOrder).Label("Sort Order").Width(90);
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery()
  {
    Query.SortBy(r => r.SortOrder).ThenBy(r => r.Handle);
    Query.RecordLabel(e => e.Name);
    Query.SearchBy(r => r.Id, r => r.Name, r => r.Handle);
    Query.NamedFilter("State", L["Status"])
      .All(L["All"]).Default()
      .Item("Active", L["Active"], r => r.IsActive).Color(UiColor.Emerald)
      .Item("Archived", L["Disabled"], r => !r.IsActive).Color(UiColor.Slate);
  }

  protected override void SetHooks() =>
    Hooks.BeforeDelete(async (c, ct) =>
    {
      foreach (var rec in c.Records)
      {
        var isBuiltIn = AgentModelCatalog.DefaultProviders()
          .Any(p => string.Equals(p.Handle, rec.Handle, StringComparison.OrdinalIgnoreCase));
        var hasModels = await db.AppAgentModel
          .AnyAsync(m => m.ProviderHandle == rec.Handle, ct);

        if (isBuiltIn || hasModels)
        {
          c.Refuse(L["Cannot delete a provider that has models or is built-in."], rec.Id);
          return;
        }
      }
    });

  protected override void SetActions() =>
    Actions.Add("ToggleActive")
      .AllowEdit()
      .Label("Toggle active")
      .Icon(UiIcon.Edit)
      .Do(async (ctx, ct) =>
      {
        var activeIds = (await db.AppAgentProvider
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(ct))
          .ToHashSet();

        foreach (var rec in ctx.Records)
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

        await ctx.FlushAsync(ct);
        await AgentModelCatalog.SyncProviderModelsStateAsync(db, ct);
      });
}
