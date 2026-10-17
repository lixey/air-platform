using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp;

public sealed class AppLanguageGridService(DemoDbContext db) : GridService<AppLanguage, AppLanguage>(db)
{
  public override Feature Feature => App.Language;

  protected override void SetColumns(Columns<AppLanguage> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Handle).Label("Code");
    columns.Add(r => r.Name).Label("Language");
  }
}

public sealed class AppColorGridService(DemoDbContext db) : GridService<AppColor, AppColor>(db)
{
  public override Feature Feature => App.Color;

  protected override void SetColumns(Columns<AppColor> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Handle).Label("Color Key");
    columns.Add(r => r.Name).Label("Color Name");
  }
}
