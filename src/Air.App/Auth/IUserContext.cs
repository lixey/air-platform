namespace Air.App.Auth;

/// <summary>
/// The principal of the current request: identity, session and permissions, how it was
/// authenticated, and the claims the application added. It knows no role: a right decides every check.
/// </summary>
public interface IUserContext
{
  long? UserId { get; }
  long? SessionId { get; }
  string? Login { get; }
  int TokenVersion { get; }
  bool IsAuthenticated { get; }
  bool MustChangePassword => false;
  string Language { get; }
  IReadOnlySet<string> Permissions { get; }

  /// <summary>
  /// Whether the principal holds the permission: the key itself, the feature's {Module}.{Feature}.*,
  /// the module's {Module}.* or everything's *. A pattern asked as a key is never held.
  /// </summary>
  bool Can(string permission);

  /// <summary>Whether the principal holds the right, without naming its key by hand.</summary>
  bool Can(Right right) => Can(right.Key);

  /// <summary>The value of a claim of the principal, or null.</summary>
  string? Claim(string type);
}
