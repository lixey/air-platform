namespace Platform.Api.Modules;

public abstract class FeatureAccess
{
  public const string AccessToPrefix = "AccessTo";

  internal Feature Feature { get; set; } = null!;

  internal void RegisterRights()
  {
    foreach (var prop in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      if (prop.PropertyType == typeof(Right) && prop.GetMethod != null && prop.GetIndexParameters().Length == 0)
      {
        _ = prop.GetValue(this);
      }
    }
  }

  protected Right Right([CallerMemberName] string name = "")
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    return Feature.Action(AccessToPrefix + name);
  }
}
