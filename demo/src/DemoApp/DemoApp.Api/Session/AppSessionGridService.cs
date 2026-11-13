namespace DemoApp.Session;

public sealed class AppSessionGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAuthSession>(db, app)
{
  public override Feature Feature => App.Session;

  protected override void SetParent() => Parent.Bind<AppUserFormService>(e => e.UserId);

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id);
    Columns.Add(r => r.Client);
    Columns.Add(r => r.Address);
    Columns.Add(r => r.LastUsedAt);
    Columns.Add(r => r.ExpiresAt);
    Columns.Add(r => r.RevokedAt);
    Columns.Add(r => r.RevokedReason);
  }
}
