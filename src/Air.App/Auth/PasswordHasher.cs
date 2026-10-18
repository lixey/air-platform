using Microsoft.AspNetCore.Identity;

namespace Air.App.Auth;

/// <summary>
/// Built-in PBKDF2 password hasher implementation based on ASP.NET Core Identity.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
  private readonly PasswordHasher<object> _hasher = new();
  private static readonly object DummyUser = new();

  public string HashPassword(string password)
  {
    ArgumentException.ThrowIfNullOrEmpty(password);
    return _hasher.HashPassword(DummyUser, password);
  }

  public PasswordVerification VerifyPassword(string password, string passwordHash)
  {
    if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
    {
      return PasswordVerification.Failed;
    }

    try
    {
      return _hasher.VerifyHashedPassword(DummyUser, passwordHash, password) switch
      {
        PasswordVerificationResult.Success => PasswordVerification.Success,
        PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
        _ => PasswordVerification.Failed
      };
    }
    catch
    {
      return PasswordVerification.Failed;
    }
  }
}
