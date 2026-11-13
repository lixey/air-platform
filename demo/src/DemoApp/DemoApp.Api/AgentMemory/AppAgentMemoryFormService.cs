namespace DemoApp.AgentMemory;

public sealed class AppAgentMemoryFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentMemory>(db, app)
{
  public override Feature Feature => App.AgentMemory;

  protected override void SetElements()
  {
    Elements.Add(e => e.Title).Focus().Span(6);
    Elements.Add(e => e.Kind).Span(3);
    Elements.Add(e => e.Module).Span(3);
    Elements.Add(e => e.Priority).Span(4);
    Elements.Add(e => e.IsPinned).Label("Pinned").AsCheckbox().Span(4);
    Elements.Add(e => e.IsActive).AsCheckbox().Span(4);
    Elements.Add(e => e.Content).AsTextarea().Span(12);
  }

  protected override void SetHooks()
  {
    Hooks.AfterLoad((c, _) =>
    {
      if (c.IsNew)
      {
        c.Entity.Kind = AgentMemoryKinds.Fact;
        c.Entity.Priority = 100;
        c.Entity.IsActive = true;
      }

      return Task.CompletedTask;
    });

    Hooks.BeforeSave((c, _) =>
    {
      CodeRows.EnsurePascalCaseHandle(c.Entity.Kind, nameof(AppAgentMemory));
      c.Entity.UpdatedAt = Time.UtcNow;
      return Task.CompletedTask;
    });
  }
}
