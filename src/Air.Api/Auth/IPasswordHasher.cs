namespace Platform.Api.Auth;

/// <summary>
/// Hashes and verifies user passwords using ASP.NET Core Identity PBKDF2 hasher.
/// </summary>
public interface IPasswordHasher
{
  string HashPassword(string password);
  PasswordVerification VerifyPassword(string password, string passwordHash);
}
