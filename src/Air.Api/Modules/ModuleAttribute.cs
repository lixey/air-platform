namespace Platform.Api.Modules;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleAttribute : Attribute
{
  public ModuleAttribute(string handle)
  {
    Platform.Data.CodeRows.EnsurePascalCaseHandle(handle, "Module");
    Handle = handle;
  }

  public string Handle { get; }
  public string? Name { get; set; }
  public string? Icon { get; set; }
}
