namespace Platform.Api.Host.Internals;

internal sealed record EndpointParameter(
  string Name,
  Type ParameterType,
  EndpointParameterKind Kind,
  bool HasDefaultValue,
  object? DefaultValue);
