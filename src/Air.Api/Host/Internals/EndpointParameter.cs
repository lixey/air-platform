namespace Platform.Api.Host.Internals;

internal sealed record EndpointParameter(
  string Name,
  Type ParameterType,
  EndpointParameterKind Kind,
  bool IsNullable,
  bool HasDefaultValue,
  object? DefaultValue,
  Func<object>? Factory = null);
