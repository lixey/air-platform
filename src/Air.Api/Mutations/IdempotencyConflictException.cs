namespace Platform.Api.Mutations;

public sealed class IdempotencyConflictException(string message) : InvalidOperationException(message);
