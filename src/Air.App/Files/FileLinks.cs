using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Air.App.Files.Internal;
using Air.Data.Entities;
using Air.Data.Files;

namespace Air.App.Files;

public sealed class FileLinks
{
  public const string Segment = "platform-files";
  private const int MaxTokenLength = 4096;
  private const int MacBytes = 32;

  private readonly string _prefix = $"/api/{Segment}/";
  private readonly TimeSpan _lifetime = TimeSpan.FromHours(24);

  public string Url(BaseAppFile file)
  {
    ArgumentNullException.ThrowIfNull(file);
    return _prefix + Sign(file.StorageKey, file.ContentType, file.Name, DateTimeOffset.UtcNow + _lifetime, file.Store);
  }

  public string? TryUrl(BaseAppFile file) => CanSign(file.StorageKey, file.ContentType, file.Name) ? Url(file) : null;

  public string? TryThumbnailUrl(BaseAppFile file) =>
    file.ThumbnailKey is { } key && CanSign(key, file.ContentType, file.Name)
      ? _prefix + Sign(key, file.ContentType, file.Name, DateTimeOffset.UtcNow + _lifetime, file.Store, isThumbnail: true)
      : null;

  internal string Sign(string key, string contentType, string name, DateTimeOffset expires, string? store = null, bool isThumbnail = false)
  {
    if (!CanSign(key, contentType, name))
    {
      throw new ArgumentException("Invalid file key, content type or name for link signing", nameof(key));
    }

    var link = new FileLink(key, store, contentType, name, expires.ToUnixTimeSeconds(), isThumbnail);
    var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(link));
    return $"{payload}.{Base64Url.EncodeToString(Mac(payload))}";
  }

  internal FileLink? Read(string? token)
  {
    if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) return null;

    var dot = token.IndexOf('.');
    if (dot <= 0 || dot != token.LastIndexOf('.')) return null;

    var payload = token[..dot];
    byte[] mac;
    byte[] json;
    try
    {
      mac = Base64Url.DecodeFromChars(token.AsSpan(dot + 1));
      json = Base64Url.DecodeFromChars(payload);
    }
    catch (FormatException)
    {
      return null;
    }

    if (mac.Length != MacBytes || !CryptographicOperations.FixedTimeEquals(mac, Mac(payload)))
    {
      return null;
    }

    FileLink? link;
    try
    {
      link = JsonSerializer.Deserialize<FileLink>(json);
    }
    catch (JsonException)
    {
      return null;
    }

    return link is not null && CanSign(link.Key, link.ContentType, link.Name) && link.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
      ? link
      : null;
  }

  private static byte[] Mac(string payload) =>
    HMACSHA256.HashData(Auth.SessionTokenService.DeriveSubKey("AirPlatform.FileLinks"), Encoding.ASCII.GetBytes(payload));

  private static bool CanSign(string? key, string? contentType, string? name) =>
    FileKeys.IsValid(key) && contentType is not null && FileTypeTable.IsKnown(contentType) &&
    name is not null && FileTypeTable.NameOf(name) == name;
}
