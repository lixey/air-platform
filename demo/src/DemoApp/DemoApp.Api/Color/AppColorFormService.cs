namespace DemoApp.Color;

public sealed class AppColorFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppColor>(db, app)
{
  public override Feature Feature => App.Color;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.Handle);
  }
}
