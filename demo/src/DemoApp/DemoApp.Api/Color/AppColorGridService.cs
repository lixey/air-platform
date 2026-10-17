using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp.Color;

public sealed class AppColorGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppColor>(db, app)
{
  public override Feature Feature => App.Color;

  protected override void SetColumns(Columns<AppColor> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppColorFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Handle);
  }
}
