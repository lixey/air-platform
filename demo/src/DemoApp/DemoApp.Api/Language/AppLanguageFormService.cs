namespace DemoApp.Language;

public sealed class AppLanguageFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppLanguage>(db, app)
{
  public override Feature Feature => App.Language;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.Handle);
    Elements.Add(e => e.Code);
    Elements.Add(e => e.Description);
    Elements.Add(e => e.IsActive).AsCheckbox();
  }
}
