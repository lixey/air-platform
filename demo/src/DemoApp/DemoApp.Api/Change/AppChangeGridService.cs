namespace DemoApp.Change;

/// <summary>
/// The change journal of the active tenant, read only.
/// </summary>
public sealed class AppChangeGridService(AppDbContext db, AppService? app = null) : ChangeGridService(db, app)
{
  public override Feature Feature => App.Change;
}
