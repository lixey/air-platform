namespace Platform.App.Tests.Internals;

internal sealed class TestAutoDrawerTabFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestTag>(db, app)
{
  public override Feature Feature => TestAppModule.Tags;

  protected override void SetElements(Elements elements)
  {
    elements.Add(t => t.Name).Required();
    elements.AddTab("Details");
    elements.Add(t => t.DefaultPrice);
  }
}
