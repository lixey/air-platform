namespace Platform.Api.Modules;

[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class EndpointAttribute(string? name = null) : Attribute
{
  public string? Name { get; } = name;
  public EndpointAuth Auth { get; init; } = EndpointAuth.Default;
  public bool RequireClientHeader { get; init; }
  public Type? RequestType { get; init; }
  public Type? ResponseType { get; init; }
  public bool ResponseNullable { get; init; }
}
