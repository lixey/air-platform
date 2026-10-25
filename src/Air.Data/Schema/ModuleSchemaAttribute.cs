namespace Platform.Data.Schema;

/// <summary>
/// Declares the module handle whose schema and migrations this <see cref="PlatformDbContext"/> level owns.
/// Non-inherited so each derived module context in a hierarchy declares its own module level.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModuleSchemaAttribute : Attribute
{
  public ModuleSchemaAttribute(string handle)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(handle);
    Handle = handle.Trim();
  }

  public string Handle { get; }
}
