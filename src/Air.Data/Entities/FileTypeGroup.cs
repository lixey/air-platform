namespace Air.Data.Entities;

public static class FileTypeGroup
{
  public const string Pictures = "@pictures";
  public const string Office = "@office";
  public const string Text = "@text";
  public const string PhoneVideo = "@phone-video";
  public const string All = "@all";

  private static readonly Dictionary<string, IReadOnlyList<string>> Types = new(StringComparer.Ordinal)
  {
    [Pictures] = [FileType.Png, FileType.Jpeg, FileType.Gif, FileType.WebP],
    [Office] = [FileType.Doc, FileType.Docx, FileType.Xls, FileType.Xlsx, FileType.Pptx, FileType.Odt, FileType.Ods],
    [Text] = [FileType.Text, FileType.Csv, FileType.Markdown],
    [PhoneVideo] = [FileType.Mp4, FileType.Mov, FileType.WebM, FileType.ThreeGp],
    [All] = FileType.Known
  };

  internal static bool IsGroup(string? value) => value is ['@', ..];

  public static IReadOnlyList<string>? TypesOf(string group) => Types.GetValueOrDefault(group);
}
