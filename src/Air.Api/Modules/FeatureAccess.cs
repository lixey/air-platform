namespace Platform.Api.Modules;

public abstract class FeatureAccess
{
  public const string AccessToPrefix = "AccessTo";

  internal Feature Feature { get; set; } = null!;

  protected Right Right([CallerMemberName] string name = "")
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    return Feature.Action(AccessToPrefix + name);
  }
}
