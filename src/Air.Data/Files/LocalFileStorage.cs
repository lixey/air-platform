namespace Platform.Data.Files;

public sealed class LocalFileStorage : IFileStorage
{
  private readonly string _baseDirectory;

  public LocalFileStorage(string? baseDirectory = null)
  {
    _baseDirectory = baseDirectory ?? Path.Combine(AppContext.BaseDirectory, "air_files");
    Directory.CreateDirectory(_baseDirectory);
  }

  private string PathOf(string key) => Path.Combine(_baseDirectory, key);

  public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(content);
    if (!FileKeys.IsValid(key))
    {
      throw new ArgumentException("Invalid storage key", nameof(key));
    }

    var filePath = PathOf(key);
    var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
    try
    {
      await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
      {
        await content.CopyToAsync(fs, cancellationToken);
      }

      File.Move(tempPath, filePath, overwrite: true);
    }
    finally
    {
      if (File.Exists(tempPath))
      {
        try
        {
          File.Delete(tempPath);
        }
        catch { }
      }
    }
  }

  public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
  {
    if (!FileKeys.IsValid(key))
    {
      return Task.FromResult<Stream?>(null);
    }

    var filePath = PathOf(key);
    if (!File.Exists(filePath))
    {
      return Task.FromResult<Stream?>(null);
    }

    Stream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
    return Task.FromResult<Stream?>(fs);
  }

  public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
  {
    if (!FileKeys.IsValid(key))
    {
      return Task.CompletedTask;
    }

    var filePath = PathOf(key);
    if (File.Exists(filePath))
    {
      try
      {
        File.Delete(filePath);
      }
      catch { }
    }

    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<string>> KeysOlderThanAsync(DateTimeOffset moment, CancellationToken cancellationToken = default)
  {
    var list = new List<string>();
    if (Directory.Exists(_baseDirectory))
    {
      foreach (var file in Directory.GetFiles(_baseDirectory))
      {
        var info = new FileInfo(file);
        if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
          if (info.CreationTimeUtc < moment.UtcDateTime)
          {
            try
            {
              File.Delete(file);
            }
            catch { }
          }

          continue;
        }

        if (info.CreationTimeUtc < moment.UtcDateTime)
        {
          list.Add(Path.GetFileName(file));
        }
      }
    }

    return Task.FromResult<IReadOnlyList<string>>(list);
  }
}
