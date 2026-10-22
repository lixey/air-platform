namespace DemoApp.Change;

/// <summary>
/// The change journal of the active tenant, read only.
/// </summary>
public sealed class AppChangeGridService(AppDbContext db, IServiceProvider services) : ChangeGridService(db, services)
{
  public override Feature Feature => App.Change;
}
