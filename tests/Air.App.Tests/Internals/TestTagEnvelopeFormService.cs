namespace Platform.App.Tests.Internals;

internal sealed class TestTagEnvelopeFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestTag>(db, app)
{
  public override Feature Feature => TestAppModule.Tags;

  protected override void SetElements()
  {
    Elements.Add(t => t.Name).Required();
    Elements.Add(t => t.Entity);
    Elements.Add(t => t.Value);
    Elements.Add(t => t.DefaultPrice);
  }
}
