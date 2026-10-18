namespace Air.Data.Files;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FileStoreAttribute(string key) : Attribute
{
  public string Key { get; } = key;
}
