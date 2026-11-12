namespace DemoApp.Language;

public sealed class AppLanguageFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppLanguage>(db, app)
{
  public override Feature Feature => App.Language;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.Handle);
    elements.Add(e => e.Code);
    elements.Add(e => e.Description);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
