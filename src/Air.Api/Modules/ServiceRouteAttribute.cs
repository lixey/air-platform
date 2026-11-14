namespace Platform.Api.Modules;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceRouteAttribute(string route) : Attribute
{
  public string Route { get; } = route.Trim('/');
}
