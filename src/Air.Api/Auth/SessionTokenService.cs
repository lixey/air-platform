using System.Security.Cryptography;

namespace Platform.Api.Auth;

/// <summary>
/// Fast HMAC-SHA256 signed session token generator and validator.
/// </summary>
public static class SessionTokenService
{
  private static byte[] _secretKey = DeriveDefaultKey();

  public static void ConfigureSecret(string? secret)
  {
    if (!string.IsNullOrWhiteSpace(secret))
    {
      _secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
  }

  internal static byte[] DeriveSubKey(string purpose) =>
    HMACSHA256.HashData(_secretKey, Encoding.UTF8.GetBytes(purpose));

  private static byte[] DeriveDefaultKey()
  {
    var envSecret = Environment.GetEnvironmentVariable("AIR_TOKEN_SECRET");
    if (!string.IsNullOrWhiteSpace(envSecret))
    {
      return SHA256.HashData(Encoding.UTF8.GetBytes(envSecret));
    }

    var entropy = $"{Environment.MachineName}:{Environment.ProcessPath}:{AppContext.BaseDirectory}:AirPlatform.SessionKey";
    return SHA256.HashData(Encoding.UTF8.GetBytes(entropy));
  }

  public static string CreateAccessToken(long sessionId, long userId, DateTimeOffset expiresAt, int tokenVersion = 1)
  {
    var payload = $"{sessionId}:{userId}:{tokenVersion}:{expiresAt.ToUnixTimeSeconds()}";
    var signature = Sign(payload);
    return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}:{signature}"));
  }

  public static bool TryValidateAccessToken(string token, out long sessionId, out long userId, out DateTimeOffset expiresAt) =>
    TryValidateAccessToken(token, out sessionId, out userId, out expiresAt, out _);

  public static bool TryValidateAccessToken(string token, out long sessionId, out long userId, out DateTimeOffset expiresAt, out int tokenVersion)
  {
    sessionId = 0;
    userId = 0;
    expiresAt = default;
    tokenVersion = 1;

    try
    {
      var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
      var parts = raw.Split(':');
      if (parts.Length is not (4 or 5))
      {
        return false;
      }

      if (!long.TryParse(parts[0], out sessionId) ||
          !long.TryParse(parts[1], out userId))
      {
        return false;
      }

      long unixExp;
      string payload;
      string sigPart;

      if (parts.Length == 5)
      {
        if (!int.TryParse(parts[2], out tokenVersion) || !long.TryParse(parts[3], out unixExp))
        {
          return false;
        }

        payload = $"{parts[0]}:{parts[1]}:{parts[2]}:{parts[3]}";
        sigPart = parts[4];
      }
      else
      {
        if (!long.TryParse(parts[2], out unixExp))
        {
          return false;
        }

        payload = $"{parts[0]}:{parts[1]}:{parts[2]}";
        sigPart = parts[3];
      }

      expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixExp);
      if (expiresAt <= DateTimeOffset.UtcNow)
      {
        return false;
      }

      var expectedSig = Sign(payload);
      return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(expectedSig),
        Encoding.UTF8.GetBytes(sigPart)
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
    using var hmac = new HMACSHA256(_secretKey);
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    return Convert.ToHexString(hash).ToLowerInvariant();
  }
}
