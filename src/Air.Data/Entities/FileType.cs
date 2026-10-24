namespace Platform.Data.Entities;

public static class FileType
{
  public const string Png = "image/png";
  public const string Jpeg = "image/jpeg";
  public const string Gif = "image/gif";
  public const string WebP = "image/webp";
  public const string Svg = "image/svg+xml";
  public const string Pdf = "application/pdf";
  public const string Text = "text/plain";
  public const string Csv = "text/csv";
  public const string Markdown = "text/markdown";
  public const string Html = "text/html";
  public const string Json = "application/json";
  public const string Xml = "application/xml";
  public const string Zip = "application/zip";
  public const string Doc = "application/msword";
  public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
  public const string Xls = "application/vnd.ms-excel";
  public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
  public const string Pptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
  public const string Odt = "application/vnd.oasis.opendocument.text";
  public const string Ods = "application/vnd.oasis.opendocument.spreadsheet";
  public const string Mp3 = "audio/mpeg";
  public const string Mp4 = "video/mp4";
  public const string Mov = "video/quicktime";
  public const string WebM = "video/webm";
  public const string ThreeGp = "video/3gpp";

  public static readonly IReadOnlyList<string> Known =
  [
    Png, Jpeg, Gif, WebP, Svg, Pdf, Text, Csv, Markdown, Html, Json, Xml, Zip, Doc, Docx, Xls, Xlsx, Pptx, Odt, Ods, Mp3, Mp4,
    Mov, WebM, ThreeGp
  ];
}
