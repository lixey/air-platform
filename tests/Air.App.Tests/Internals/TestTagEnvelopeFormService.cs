namespace Platform.App.Tests.Internals;

internal sealed class TestTagEnvelopeFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestTag>(db, app)
{
  public override Feature Feature => TestAppModule.Tags;

  protected override void SetElements(Elements elements)
  {
    elements.Add(t => t.Name).Required();
    elements.Add(t => t.Entity);
    elements.Add(t => t.Value);
    elements.Add(t => t.DefaultPrice);
  }
}
