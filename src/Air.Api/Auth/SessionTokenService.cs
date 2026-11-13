using System.Security.Cryptography;

namespace Platform.Api.Auth;

/// <summary>
/// Fast HMAC-SHA256 signed session token generator and validator.
/// </summary>
public static class SessionTokenService
{
  public const string SecretEnvironmentVariable = "PLATFORM_TOKEN_SECRET";

  private static byte[] SecretKey { get => field ??= DeriveDefaultKey(); set; }

  public static void ConfigureSecret(string? secret)
  {
    if (!string.IsNullOrWhiteSpace(secret))
    {
      var trimmed = secret.Trim();
      if (trimmed.Length < 32)
      {
        throw new InvalidOperationException("Token secret must be at least 32 characters long.");
      }

      SecretKey = SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
    }
  }

  internal static void EnsureConfiguredForEnvironment(string? secret, bool isProduction)
  {
    if (string.IsNullOrWhiteSpace(secret))
    {
      if (isProduction)
      {
        throw new InvalidOperationException(
          $"Production startup requires a token signing secret (>= 32 characters) configured via 'Auth:TokenSecret', 'Platform:Security:Jwt:Secret', or '{SecretEnvironmentVariable}'.");
      }

      return;
    }

    ConfigureSecret(secret);
  }

  internal static byte[] DeriveSubKey(string purpose) =>
    HMACSHA256.HashData(SecretKey, Encoding.UTF8.GetBytes(purpose));

  private static byte[] DeriveDefaultKey()
  {
    var envSecret = Environment.GetEnvironmentVariable(SecretEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(envSecret))
    {
      var trimmed = envSecret.Trim();
      if (trimmed.Length < 32)
      {
        throw new InvalidOperationException($"{SecretEnvironmentVariable} must be at least 32 characters long.");
      }

      return SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
    }

    var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
      ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    if (string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException(
        $"Production startup requires a token signing secret (>= 32 characters) configured via 'Auth:TokenSecret', 'Platform:Security:Jwt:Secret', or '{SecretEnvironmentVariable}'.");
    }

    try
    {
      var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      if (!string.IsNullOrWhiteSpace(userDir))
      {
        var keyDir = Path.Combine(userDir, ".platform");
        Directory.CreateDirectory(keyDir);
        var keyFile = Path.Combine(keyDir, "token.key");
        if (File.Exists(keyFile))
        {
          var existing = File.ReadAllBytes(keyFile);
          if (existing.Length >= 32)
          {
            return SHA256.HashData(existing);
          }
        }

        var generated = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyFile, generated);
        if (!OperatingSystem.IsWindows())
        {
          File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return SHA256.HashData(generated);
      }
    }
    catch
    {
      // Fallback to in-memory CSPRNG key if filesystem is read-only
    }

    return RandomNumberGenerator.GetBytes(32);
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
      if (expiresAt <= TimeProvider.System.GetUtcNow())
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
    using var hmac = new HMACSHA256(SecretKey);
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    return Convert.ToHexString(hash).ToLowerInvariant();
  }
}
