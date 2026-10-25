namespace Platform.Api.Auth;

/// <summary>
/// Allows application modules to enrich the cached user authentication profile with custom data
/// (e.g., employee record, departments, positions, responsibility centers, or row-level restrictions).
/// </summary>
public interface IUserAuthEnricher
{
  Task EnrichAsync(long userId, long? tenantId, IDictionary<string, JsonElement> data, CancellationToken ct = default);
}
