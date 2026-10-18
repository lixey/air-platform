using System.Buffers.Text;
using System.Security.Cryptography;
using Air.Data.Entities;

namespace Air.Data.Files;

public static class FileKeys
{
  private const int RandomBytes = 32;

  public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes));

  public static bool IsValid(string? key) =>
    key is { Length: BaseAppFile.StorageKeyLength } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
