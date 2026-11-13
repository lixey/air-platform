namespace DemoApp.Language;

public sealed class AppLanguageGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppLanguage>(db, app)
{
  public override Feature Feature => App.Language;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<AppLanguageFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Handle);
    Columns.Add(r => r.Code);
  }
}
