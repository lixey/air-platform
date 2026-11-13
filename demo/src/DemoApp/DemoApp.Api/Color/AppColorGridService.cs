namespace DemoApp.Color;

public sealed class AppColorGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppColor>(db, app)
{
  public override Feature Feature => App.Color;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<AppColorFormService>();
    Columns.Add(r => r.Name).Color(c => c.Handle);
    Columns.Add(r => r.Handle);
  }
}
