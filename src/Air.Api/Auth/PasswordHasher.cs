using Microsoft.AspNetCore.Identity;

namespace Platform.Api.Auth;

/// <summary>
/// Built-in PBKDF2 password hasher implementation based on ASP.NET Core Identity.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
  private static readonly SemaphoreSlim ConcurrencyGate = new(Math.Max(2, Environment.ProcessorCount));
  private static readonly object DummyUser = new();
  private readonly PasswordHasher<object> _hasher = new();

  public string HashPassword(string password)
  {
    ArgumentException.ThrowIfNullOrEmpty(password);
    ConcurrencyGate.Wait();
    try
    {
      return _hasher.HashPassword(DummyUser, password);
    }
    finally
    {
      ConcurrencyGate.Release();
    }
  }

  public PasswordVerification VerifyPassword(string password, string passwordHash)
  {
    if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
    {
      return PasswordVerification.Failed;
    }

    ConcurrencyGate.Wait();
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
    finally
    {
      ConcurrencyGate.Release();
    }
  }
}
