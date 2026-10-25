namespace DemoApp.AgentMemory;

public sealed class AppAgentMemoryFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentMemory>(db, app)
{
  public override Feature Feature => App.AgentMemory;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Popover(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Title).Focus().Span(6);
    elements.Add(e => e.Kind).Span(3);
    elements.Add(e => e.Module).Span(3);
    elements.Add(e => e.Priority).Span(4);
    elements.Add(e => e.IsPinned).Label("Pinned").AsCheckbox().Span(4);
    elements.Add(e => e.IsActive).AsCheckbox().Span(4);
    elements.Add(e => e.Content).AsTextarea().Span(12);
  }

  protected override void SetHooks(Hooks hooks)
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
      c.Entity.UpdatedAt = DateTimeOffset.UtcNow;
      return Task.CompletedTask;
    });
  }
}
