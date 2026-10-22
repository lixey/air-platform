namespace DemoApp.Language;

public sealed class AppLanguageGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppLanguage>(db, app)
{
  public override Feature Feature => App.Language;

  protected override void SetColumns(Columns<AppLanguage> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppLanguageFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Handle);
    columns.Add(r => r.Code);
  }
}
