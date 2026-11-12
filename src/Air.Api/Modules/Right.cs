namespace Platform.Api.Modules;

public sealed class Right
{
  public const int MaxKeyLength = 200;
  public const string View = "View";
  public const string MenuView = "MenuView";
  public const string Edit = "Edit";
  public const string Delete = "Delete";
  public const string Export = "Export";
  public const string History = "History";
  public const string All = "*";

  public static readonly IReadOnlySet<string> StandardActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
  {
    View, MenuView, Edit, Delete, Export, History, All
  };

  internal Right(string key, string action, Feature? feature, bool isPattern = false)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    if (key.Length > MaxKeyLength)
    {
      throw new InvalidOperationException($"Right key '{key}' exceeds maximum length {MaxKeyLength}.");
    }

    Key = key;
    Action = action;
    Feature = feature;
    IsPattern = isPattern;
  }

  public string Key { get; }
  public string Action { get; }
  public Feature? Feature { get; }
  public bool IsPattern { get; }

  public override string ToString() => Key;
  public static implicit operator string(Right right) => right.Key;
}
