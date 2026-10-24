namespace Platform.Api.Files.Internals;

internal static class FileTypeTable
{
  public static readonly IReadOnlyList<string> Pictures = FileTypeGroup.TypesOf(FileTypeGroup.Pictures)!;
  public static readonly IReadOnlyList<string> ServedInline = [.. Pictures, FileType.Pdf, FileType.Mp4];
  public static readonly IReadOnlyList<string> TakesThumbnail = [.. Pictures, FileType.Mp4];
  public static readonly IReadOnlyList<string> Thumbnail = [FileType.WebP, FileType.Jpeg];

  public const long ThumbnailMaxBytes = 64 * FileSize.KiB;
  public const int ThumbnailHeadBytes = 12;
  public const long MaxPixels = 40_000_000;

  public static readonly IReadOnlyList<string> ZipDocuments = [FileType.Docx, FileType.Xlsx, FileType.Pptx, FileType.Odt, FileType.Ods];

  private static readonly Dictionary<string, string[]> Extensions = new(StringComparer.Ordinal)
  {
    [FileType.Png] = [".png"],
    [FileType.Jpeg] = [".jpg", ".jpeg"],
    [FileType.Gif] = [".gif"],
    [FileType.WebP] = [".webp"],
    [FileType.Svg] = [".svg"],
    [FileType.Pdf] = [".pdf"],
    [FileType.Text] = [".txt"],
    [FileType.Csv] = [".csv"],
    [FileType.Markdown] = [".md"],
    [FileType.Html] = [".html", ".htm"],
    [FileType.Json] = [".json"],
    [FileType.Xml] = [".xml"],
    [FileType.Zip] = [".zip"],
    [FileType.Doc] = [".doc"],
    [FileType.Docx] = [".docx"],
    [FileType.Xls] = [".xls"],
    [FileType.Xlsx] = [".xlsx"],
    [FileType.Pptx] = [".pptx"],
    [FileType.Odt] = [".odt"],
    [FileType.Ods] = [".ods"],
    [FileType.Mp3] = [".mp3"],
    [FileType.Mp4] = [".mp4"],
    [FileType.Mov] = [".mov"],
    [FileType.WebM] = [".webm"],
    [FileType.ThreeGp] = [".3gp"]
  };

  public static IEnumerable<string> Known => Extensions.Keys;
  public static bool IsKnown(string contentType) => Extensions.ContainsKey(contentType);
  public static bool IsServedInline(string contentType) => ServedInline.Contains(contentType, StringComparer.Ordinal);
  public static bool TakesAThumbnail(string contentType) => TakesThumbnail.Contains(contentType, StringComparer.Ordinal);
  public static bool IsZipDocument(string contentType) => ZipDocuments.Contains(contentType, StringComparer.Ordinal);

  public static bool StartsAsZip(ReadOnlySpan<byte> head) => head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]);

  public static string? Normalize(string? declared)
  {
    var type = declared?.Split(';', 2)[0].Trim().ToLowerInvariant();
    return string.IsNullOrEmpty(type) ? null : type;
  }

  public static string? NameOf(string? posted)
  {
    var name = posted ?? string.Empty;
    name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
    return name.Length is 0 or > BaseAppFile.NameMaxLength || name.Any(c => char.IsControl(c) || IsBidiControl(c))
      ? null
      : name;
  }

  private static bool IsBidiControl(char c) =>
    c is '\u200E' or '\u200F' or '\u061C' or >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069';

  public static bool HasExtension(string contentType, string name) =>
    Extensions.TryGetValue(contentType, out var extensions) &&
    extensions.Contains(Path.GetExtension(name).ToLowerInvariant(), StringComparer.Ordinal);

  public static IEnumerable<string> TypesOf(IEnumerable<string> contentTypes) =>
    contentTypes
      .SelectMany(t => FileTypeGroup.TypesOf(t) ?? [t])
      .Distinct(StringComparer.Ordinal);

  public static IEnumerable<string> ExtensionsOf(IEnumerable<string> contentTypes) =>
    TypesOf(contentTypes)
      .SelectMany(t => Extensions.TryGetValue(t, out var extensions) ? extensions : [])
      .Distinct(StringComparer.Ordinal);

  public static string? ThumbnailTypeOf(ReadOnlySpan<byte> head)
  {
    foreach (var type in Thumbnail)
    {
      if (StartsAsDeclared(type, head))
      {
        return type;
      }
    }

    return null;
  }

  public static bool StartsAsDeclared(string contentType, ReadOnlySpan<byte> head) =>
    contentType switch
    {
      FileType.Png => head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
      FileType.Jpeg => head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
      FileType.Gif => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
      FileType.WebP => head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
      FileType.Pdf => head.StartsWith("%PDF-"u8),
      FileType.Mp4 => head.Length >= 8 && head[4..8].SequenceEqual("ftyp"u8),
      _ => true
    };
}
