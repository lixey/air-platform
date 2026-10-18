using System.Security.Cryptography;
using System.Text;

namespace Air.App.Auth;

/// <summary>
/// Fast HMAC-SHA256 signed session token generator and validator.
/// </summary>
public static class SessionTokenService
{
  private static readonly byte[] SecretKey = SHA256.HashData(Encoding.UTF8.GetBytes("AirPlatform_SessionToken_SecretKey_2026_Secure"));

  public static string CreateAccessToken(long sessionId, long userId, DateTimeOffset expiresAt)
  {
    var payload = $"{sessionId}:{userId}:{expiresAt.ToUnixTimeSeconds()}";
    var signature = Sign(payload);
    return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}:{signature}"));
  }

  public static bool TryValidateAccessToken(string token, out long sessionId, out long userId, out DateTimeOffset expiresAt)
  {
    sessionId = 0;
    userId = 0;
    expiresAt = default;

    try
    {
      var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
      var parts = raw.Split(':');
      if (parts.Length != 4) return false;

      if (!long.TryParse(parts[0], out sessionId) ||
          !long.TryParse(parts[1], out userId) ||
          !long.TryParse(parts[2], out var unixExp))
      {
        return false;
      }

      expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixExp);
      if (expiresAt <= DateTimeOffset.UtcNow)
      {
        return false;
      }

      var payload = $"{parts[0]}:{parts[1]}:{parts[2]}";
      var expectedSig = Sign(payload);
      return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(expectedSig),
        Encoding.UTF8.GetBytes(parts[3])
      );
    }
    catch
    {
      return false;
    }
  }

  public static string CreateRefreshToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

  public static string HashRefreshToken(string refreshToken)
  {
    ArgumentException.ThrowIfNullOrEmpty(refreshToken);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
  }

  private static string Sign(string payload)
  {
    using var hmac = new HMACSHA256(SecretKey);
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    return Convert.ToHexString(hash).ToLowerInvariant();
  }
}
