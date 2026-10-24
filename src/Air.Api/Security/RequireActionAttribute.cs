namespace Platform.Api.Security;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireActionAttribute : Attribute
{
  public RequireActionAttribute(EndpointAction action)
  {
    Action = action;
    ActionName = action.ToString();
  }

  public RequireActionAttribute(string actionName, bool fallbackToView = false)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
    ActionName = actionName;
    FallbackToView = fallbackToView;
    if (Enum.TryParse<EndpointAction>(actionName, ignoreCase: true, out var parsed))
    {
      Action = parsed;
    }
  }

  public EndpointAction Action { get; }
  public string ActionName { get; }
  public bool FallbackToView { get; }
}
