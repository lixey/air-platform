namespace Platform.Api.Security;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public abstract class RequireRightAttribute : Attribute
{
  private protected RequireRightAttribute(Type service, EndpointAction action)
  {
    Service = service;
    Action = action;
    ActionName = action.ToString();
  }

  private protected RequireRightAttribute(Type service, string actionName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
    Service = service;
    ActionName = actionName;
    if (Enum.TryParse<EndpointAction>(actionName, ignoreCase: true, out var parsed))
    {
      Action = parsed;
    }
  }

  public Type Service { get; }
  public EndpointAction Action { get; }
  public string ActionName { get; }
}
