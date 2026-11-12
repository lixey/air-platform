namespace Platform.Api.Security;

public sealed class RequireRightAttribute<TService>(string actionName) : RequireRightAttribute(typeof(TService), actionName)
  where TService : class;
