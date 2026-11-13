namespace Platform.Api.Modules;

[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class EndpointAttribute(string? name = null) : Attribute
{
  public string? Name { get; } = name;
  public EndpointAuth Auth { get; init; } = EndpointAuth.Default;
}
