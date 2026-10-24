namespace Platform.Api.Modules;

public abstract class ModuleDefinition
{
  public abstract string GetHandle();
  public abstract string GetName();
  public virtual string GetIcon() => "folder";
  public abstract IReadOnlyList<Feature> GetFeatures();
}
