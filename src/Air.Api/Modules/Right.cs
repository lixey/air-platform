namespace Platform.Api.Modules;

public sealed class Right
{
  public const int MaxKeyLength = 200;

  internal Right(string key, string action, Feature? feature, bool isPattern = false)
  {
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
