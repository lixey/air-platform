namespace Platform.Api.Security;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireActionAttribute : Attribute
{
  public RequireActionAttribute(EndpointAction action)
  {
    Action = action;
    ActionName = action.ToString();
  }

  public RequireActionAttribute(string actionName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
    ActionName = actionName;
    if (Enum.TryParse<EndpointAction>(actionName, ignoreCase: true, out var parsed))
    {
      Action = parsed;
    }
  }

  public EndpointAction Action { get; }
  public string ActionName { get; }
}
