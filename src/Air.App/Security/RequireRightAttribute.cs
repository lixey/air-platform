namespace Air.App.Security;

public enum EndpointAction
{
  View,
  Edit,
  Delete,
  Export
}

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

public sealed class RequireRightAttribute<TService> : RequireRightAttribute
  where TService : class
{
  public RequireRightAttribute(EndpointAction action) : base(typeof(TService), action)
  {
  }

  public RequireRightAttribute(string actionName) : base(typeof(TService), actionName)
  {
  }
}

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
