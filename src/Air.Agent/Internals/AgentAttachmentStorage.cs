namespace Platform.Agent.Internals;

internal static class AgentAttachmentStorage
{
  private static readonly HashSet<string> BlockedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".msi", ".com", ".scr", ".vbs", ".js"
  };

  private const long MaxAttachmentSizeBytes = 25 * 1024 * 1024;

  public static string FormatTenantSegment(long? tenantId) =>
    tenantId.HasValue ? $"t_{tenantId.Value.ToString(CultureInfo.InvariantCulture)}" : "t_global";

  public static string FormatUserSegment(long? userId) =>
    userId.HasValue ? $"u_{userId.Value.ToString(CultureInfo.InvariantCulture)}" : "u_anon";

  public static string GetSessionWorkspaceDirectory(string workspaceDirectory, AgentOwnerScope owner, string safeSessionId)
  {
    var dir = Path.Combine(workspaceDirectory, FormatTenantSegment(owner.TenantId), FormatUserSegment(owner.UserId), safeSessionId);
    EnsurePrivateDirectory(dir);
    return dir;
  }

  public static string GetSessionAttachmentsDirectory(string workspaceDirectory, AgentOwnerScope owner, string safeSessionId)
  {
    var dir = Path.Combine(
      workspaceDirectory,
      FormatTenantSegment(owner.TenantId),
      FormatUserSegment(owner.UserId),
      "sessions",
      safeSessionId,
      "attachments");
    EnsurePrivateDirectory(dir);
    return dir;
  }

  public static void TryDeleteSessionDirectory(string workspaceDirectory, AgentOwnerScope owner, string sessionId)
  {
    try
    {
      var sessionFolder = Path.Combine(
        workspaceDirectory,
        FormatTenantSegment(owner.TenantId),
        FormatUserSegment(owner.UserId),
        "sessions",
        sessionId);
      if (Directory.Exists(sessionFolder))
      {
        Directory.Delete(sessionFolder, recursive: true);
      }
    }
    catch { }
  }

  public static void EnsurePrivateDirectory(string path)
  {
    const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    const UnixFileMode nonOwnerMask = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                      UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    if (!OperatingSystem.IsWindows() && !Directory.Exists(path))
    {
      Directory.CreateDirectory(path, ownerOnly);
      return;
    }

    Directory.CreateDirectory(path);
    if (!OperatingSystem.IsWindows())
    {
      var currentMode = File.GetUnixFileMode(path);
      if ((currentMode & nonOwnerMask) != 0)
      {
        File.SetUnixFileMode(path, ownerOnly);
      }
    }
  }

  public static async Task<AgentAttachmentDto> WriteValidatedAttachmentAsync(
    string workspaceDirectory,
    AgentOwnerScope owner,
    string fileName,
    string contentType,
    Stream content,
    CancellationToken ct)
  {
    var rawExt = Path.GetExtension(fileName);
    if (!string.IsNullOrWhiteSpace(rawExt) && BlockedAttachmentExtensions.Contains(rawExt))
    {
      throw new InvalidOperationException(Localizer.Instance["FileTypeRefused"]);
    }

    if (content.CanSeek && content.Length > MaxAttachmentSizeBytes)
    {
      throw new InvalidOperationException(Localizer.Instance["FileTooLarge"]);
    }

    var (clampedMime, safeExt) = AgentAttachmentValidator.ClampMimeAndExtension(contentType, fileName);
    var id = Guid.NewGuid().ToString("N")[..16];
    var safeFileName = $"attachment-{id}{safeExt}";
    var dir = Path.Combine(workspaceDirectory, FormatTenantSegment(owner.TenantId), FormatUserSegment(owner.UserId), "uploads");
    EnsurePrivateDirectory(dir);
    var localPath = Path.Combine(dir, safeFileName);

    await using (var fs = File.Create(localPath))
    {
      await content.CopyToAsync(fs, ct);
      if (fs.Length > MaxAttachmentSizeBytes)
      {
        fs.Close();
        TryDeleteFile(localPath);
        throw new InvalidOperationException(Localizer.Instance["FileTooLarge"]);
      }
    }

    long fileSize;
    string sha256;
    await using (var readFs = File.OpenRead(localPath))
    {
      fileSize = readFs.Length;
      var hashBytes = await SHA256.HashDataAsync(readFs, ct);
      sha256 = Convert.ToHexStringLower(hashBytes);
    }

    return new AgentAttachmentDto
    {
      Id = id,
      FileName = string.IsNullOrWhiteSpace(fileName) ? safeFileName : Path.GetFileName(fileName),
      ContentType = clampedMime,
      Size = fileSize,
      PreviewUrl = $"/api/agent/attachments/{id}",
      LocalPath = localPath,
      Sha256 = sha256
    };
  }

  public static void CleanupStaleDiskFiles(string workspaceDirectory, DateTime cutoffUtc)
  {
    try
    {
      if (!Directory.Exists(workspaceDirectory))
      {
        return;
      }

      foreach (var file in Directory.EnumerateFiles(workspaceDirectory, "*", SearchOption.AllDirectories))
      {
        try
        {
          if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
          {
            File.Delete(file);
          }
        }
        catch { }
      }
    }
    catch { }
  }

  public static void TryDeleteFile(string? path)
  {
    if (!string.IsNullOrWhiteSpace(path))
    {
      try
      {
        if (File.Exists(path))
        {
          File.Delete(path);
        }
      }
      catch { }
    }
  }
}
