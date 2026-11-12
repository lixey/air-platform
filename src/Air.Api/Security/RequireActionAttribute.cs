namespace Platform.Api.Security;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireActionAttribute : Attribute
{
  public RequireActionAttribute(string actionName, bool fallbackToView = false)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
    ActionName = actionName;
    FallbackToView = fallbackToView;
  }

  public string ActionName { get; }
  public bool FallbackToView { get; }
}
