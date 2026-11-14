namespace Platform.Api.Host.Internals;

internal sealed class ReflectedEndpoint
{
  public required MethodInfo Method { get; init; }
  public required string ActionName { get; init; }
  public required EndpointAuth Auth { get; init; }
  public required bool IsAnonymous { get; init; }
  public required bool RequireClientHeader { get; init; }
  public required bool UsesRawHttp { get; init; }
  public required bool UsesFormFiles { get; init; }
  public Type? ExplicitRequestType { get; init; }
  public Type? ExplicitResponseType { get; init; }
  public bool ExplicitResponseNullable { get; init; }
  public required EndpointParameter[] Parameters { get; init; }
  public Func<object, object?[], Task<IResult>>? CompiledInvoker { get; init; }
}
