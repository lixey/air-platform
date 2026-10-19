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
  }

  public Type Service { get; }
  public EndpointAction Action { get; }
}

public sealed class RequireRightAttribute<TService>(EndpointAction action) : RequireRightAttribute(typeof(TService), action)
  where TService : class;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireActionAttribute(EndpointAction action) : Attribute
{
  public EndpointAction Action { get; } = action;
}
