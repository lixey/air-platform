namespace Platform.Api.Auth;

/// <summary>
/// Allows application modules to populate custom session data (such as employee profile,
/// department IDs, positions, or row-level access scopes) into the cached user snapshot.
/// </summary>
public interface ISessionClaimsProvider
{
  Task PopulateAsync(long userId, long? tenantId, IDictionary<string, JsonElement> data, CancellationToken ct = default);
}
