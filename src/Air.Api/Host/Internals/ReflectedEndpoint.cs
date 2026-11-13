namespace Platform.Api.Host.Internals;

internal sealed class ReflectedEndpoint
{
  public required MethodInfo Method { get; init; }
  public required IReadOnlyList<string> ActionNames { get; init; }
  public required EndpointAuth Auth { get; init; }
  public required bool IsAnonymous { get; init; }
  public required bool UsesRawHttp { get; init; }
  public required bool UsesFormFiles { get; init; }
  public required EndpointParameter[] Parameters { get; init; }
  public required Func<object, object?[], Task<IResult>> CompiledInvoker { get; init; }
}
