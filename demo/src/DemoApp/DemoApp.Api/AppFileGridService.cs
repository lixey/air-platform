using Air.App.Files;
using Air.Data.Entities;
using DemoApp.Data;

namespace DemoApp;

public abstract class AppFileGridService<TFile>(AppDbContext db, AppService? app = null)
  : FileGridService<TFile>(db, app)
  where TFile : BaseAppFile, new()
{
  protected AppService? AppService => app;
}
