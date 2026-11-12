namespace Platform.Agent.Internals;

internal static class AgentAttachmentValidator
{
  public const string FallbackMime = "application/octet-stream";

  private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
  {
    "image/jpeg",
    "image/png",
    "image/webp",
    "image/gif",
    "application/pdf",
    "text/plain",
    "text/csv"
  };

  private static readonly HashSet<string> InlineSafeImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
  {
    "image/jpeg",
    "image/png",
    "image/webp",
    "image/gif"
  };

  public static (string ContentType, string SafeExtension) ClampMimeAndExtension(string? rawContentType, string? fileName)
  {
    var rawExt = Path.GetExtension(fileName)?.Trim().ToLowerInvariant() ?? string.Empty;
    var rawMime = rawContentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;

    if (IsDangerousScriptOrMarkup(rawMime, rawExt))
    {
      return (FallbackMime, ".bin");
    }

    if (AllowedMimeTypes.Contains(rawMime))
    {
      return (rawMime, ExtensionForAllowedMime(rawMime, rawExt));
    }

    var inferredFromExt = rawExt switch
    {
      ".jpg" or ".jpeg" => "image/jpeg",
      ".png" => "image/png",
      ".webp" => "image/webp",
      ".gif" => "image/gif",
      ".pdf" => "application/pdf",
      ".txt" => "text/plain",
      ".csv" => "text/csv",
      _ => null
    };

    if (inferredFromExt != null &&
        (string.IsNullOrEmpty(rawMime) || string.Equals(rawMime, FallbackMime, StringComparison.OrdinalIgnoreCase)))
    {
      return (inferredFromExt, rawExt);
    }

    return (FallbackMime, ".bin");
  }

  public static string ClampStoredMime(string? storedContentType) =>
    !string.IsNullOrWhiteSpace(storedContentType) && AllowedMimeTypes.Contains(storedContentType.Trim())
      ? storedContentType.Trim().ToLowerInvariant()
      : FallbackMime;

  public static bool IsInlineSafeImage(string? contentType) =>
    !string.IsNullOrWhiteSpace(contentType) && InlineSafeImageMimeTypes.Contains(contentType.Trim());

  public static string SanitizeDownloadFileName(string? fileName, string safeExtension)
  {
    var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
    var sb = new StringBuilder();
    foreach (var ch in name)
    {
      if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ')
      {
        sb.Append(ch);
      }
    }

    var cleanBase = sb.ToString().Trim();
    if (string.IsNullOrEmpty(cleanBase))
    {
      cleanBase = "attachment";
    }

    var ext = string.IsNullOrEmpty(safeExtension) ? ".bin" : safeExtension;
    return $"{cleanBase}{ext}";
  }

  public static List<AgentAttachmentDto> StageSessionAttachments(
    IReadOnlyList<AgentAttachmentDto> attachments,
    string sessionAttachmentsDir)
  {
    Directory.CreateDirectory(sessionAttachmentsDir);
    if (attachments.Count == 0)
    {
      return [];
    }

    var staged = new List<AgentAttachmentDto>(attachments.Count);
    foreach (var att in attachments)
    {
      var clampedMime = ClampStoredMime(att.ContentType);
      var (_, safeExt) = ClampMimeAndExtension(clampedMime, att.FileName);
      var stagedFileName = $"{att.Id}_{SanitizeDownloadFileName(att.FileName, safeExt).Replace(' ', '_')}";
      var stagedPath = Path.Combine(sessionAttachmentsDir, stagedFileName);

      if (!string.IsNullOrEmpty(att.LocalPath) && File.Exists(att.LocalPath) && !File.Exists(stagedPath))
      {
        File.Copy(att.LocalPath, stagedPath, overwrite: true);
      }

      staged.Add(new AgentAttachmentDto
      {
        Id = att.Id,
        FileName = att.FileName,
        ContentType = clampedMime,
        Size = att.Size,
        PreviewUrl = att.PreviewUrl,
        LocalPath = File.Exists(stagedPath) ? stagedPath : att.LocalPath,
        Sha256 = att.Sha256
      });
    }

    return staged;
  }

  private static bool IsDangerousScriptOrMarkup(string rawMime, string rawExt) =>
    rawMime is "text/html" or "application/xhtml+xml" or "image/svg+xml" or "text/xml" or "application/xml" or "text/javascript" or "application/javascript" ||
    rawExt is ".html" or ".htm" or ".xhtml" or ".svg" or ".svgz" or ".xml" or ".js" or ".mjs";

  private static string ExtensionForAllowedMime(string mime, string rawExt) => mime switch
  {
    "image/jpeg" => rawExt is ".jpg" or ".jpeg" ? rawExt : ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    "image/gif" => ".gif",
    "application/pdf" => ".pdf",
    "text/plain" => ".txt",
    "text/csv" => ".csv",
    _ => ".bin"
  };
}
