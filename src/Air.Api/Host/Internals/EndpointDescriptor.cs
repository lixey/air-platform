namespace Platform.Api.Host.Internals;

internal sealed class EndpointDescriptor
{
  public required string RoutePattern { get; init; }
  public required string ServiceName { get; init; }
  public required string ActionName { get; init; }
  public required Type ServiceType { get; init; }
  public Type? DbContextType { get; init; }
  public Feature? StaticFeature { get; init; }
  public MethodInfo? Method { get; init; }
  public EndpointAuth Auth { get; init; }
  public bool IsAnonymous { get; init; }
  public bool RequireClientHeader { get; init; }
  public bool UsesRawHttp { get; init; }
  public bool UsesFormFiles { get; init; }
  public Type? ExplicitRequestType { get; init; }
  public Type? ExplicitResponseType { get; init; }
  public bool ExplicitResponseNullable { get; init; }
  public bool IsDynamicAction { get; init; }
  public EndpointParameter[] Parameters { get; init; } = [];
  public Func<object, object?[], Task<IResult>>? CompiledInvoker { get; init; }
}
