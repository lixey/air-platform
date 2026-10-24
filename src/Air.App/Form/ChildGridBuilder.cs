namespace Platform.App.Form;

public sealed class ChildGridBuilder(string servicePath, Type? serviceType = null, string? tab = null)
{
  public string ServicePath { get; } = servicePath;
  public Type? ServiceType => serviceType;
  public string? Tab => tab;
  public string DeclaredKey { get; private set; } = servicePath;
  public string DeclaredLabel { get; private set; } = string.Empty;
  public bool IsFiles { get; private set; }
  public string Look { get; private set; } = FilesLook.Tiles;

  public ChildGridBuilder Key(string key)
  {
    DeclaredKey = key;
    return this;
  }
  public ChildGridBuilder Label(string label)
  {
    DeclaredLabel = label;
    return this;
  }
  public ChildGridBuilder AsFiles(string look = FilesLook.Tiles)
  {
    IsFiles = true;
    Look = look;
    return this;
  }
}
