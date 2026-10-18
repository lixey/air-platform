using System.Collections.Concurrent;
using System.Reflection;
using Air.Data.Files;

namespace Air.Data.Entities;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class FileRulesAttribute : Attribute
{
  public const int DefaultMaxFiles = 100;
  public const int DefaultImageMaxSide = 2560;
  public const int ImageMaxSideMin = 640;
  public const int ImageMaxSideMax = 8192;
  public const double DefaultImageQuality = 0.8;
  public const double ImageQualityMin = 0.1;
  public const double ImageQualityMax = 1.0;

  private static readonly ConcurrentDictionary<Type, FileRulesAttribute> Checked = new();

  public FileRulesAttribute() { }

  protected FileRulesAttribute(Type store)
  {
    StoreType = store;
  }

  public long MaxBytes { get; set; }
  public string[] ContentTypes { get; set; } = [];
  public int MaxFiles { get; set; } = DefaultMaxFiles;
  public long MaxTotalBytes { get; set; }
  public long SourceMaxBytes { get; set; }
  public string Images { get; set; } = FileImages.Keep;
  public int ImageMaxSide { get; set; } = DefaultImageMaxSide;
  public double ImageQuality { get; set; } = DefaultImageQuality;
  public string Cover { get; set; } = FileCover.Manual;

  public Type? StoreType { get; }

  public static FileRulesAttribute Of(Type entityType) =>
    Checked.GetOrAdd(entityType, t =>
    {
      var attr = t.GetCustomAttribute<FileRulesAttribute>(inherit: true)
        ?? throw new InvalidOperationException($"File entity '{t.Name}' declares no [FileRules].");
      return attr;
    });

  public static FileRulesAttribute? TryGet(Type entityType) =>
    entityType.GetCustomAttribute<FileRulesAttribute>(inherit: true);
}
