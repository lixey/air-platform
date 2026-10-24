namespace Platform.Mcp.Contracts;

public sealed record McpCallContext(
  long? TenantId,
  long? UserId,
  IReadOnlySet<string>? Rights,
  string? SessionId);
