namespace Platform.App.Tests.Internals;

internal sealed class TestAutoDrawerTabFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestTag>(db, app)
{
  public override Feature Feature => TestAppModule.Tags;

  protected override void SetElements()
  {
    Elements.Add(t => t.Name).Required();
    Elements.AddTab("Details");
    Elements.Add(t => t.DefaultPrice);
  }
}
