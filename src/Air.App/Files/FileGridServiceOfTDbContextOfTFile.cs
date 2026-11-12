namespace Platform.App.Files;

public abstract class FileGridService<TDbContext, TFile>(TDbContext db, PlatformAppService? app = null)
  : FileGridService<TFile>(db, app)
  where TDbContext : PlatformDbContext
  where TFile : BaseAppFile, new()
{
  protected TDbContext Db => db;
}
