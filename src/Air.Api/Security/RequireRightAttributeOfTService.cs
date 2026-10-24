namespace Platform.Api.Security;

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
