namespace DemoApp.Data.Models;

[Index(nameof(Handle), IsUnique = true)]
public sealed class AppTenant : BaseAppTenant
{
  public AppTenant? Parent { get; set; }
}
