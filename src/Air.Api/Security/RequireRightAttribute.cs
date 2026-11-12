namespace Platform.Api.Security;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public abstract class RequireRightAttribute : Attribute
{
  private protected RequireRightAttribute(Type service, string actionName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
    Service = service;
    ActionName = actionName;
  }

  public Type Service { get; }
  public string ActionName { get; }
}
