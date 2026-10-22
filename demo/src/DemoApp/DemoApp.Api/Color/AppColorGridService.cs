namespace DemoApp.Color;

public sealed class AppColorGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppColor>(db, app)
{
  public override Feature Feature => App.Color;

  protected override void SetColumns(Columns<AppColor> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppColorFormService>();
    columns.Add(r => r.Name).Color(c => c.Handle);
    columns.Add(r => r.Handle);
  }
}
