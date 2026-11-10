namespace Platform.Api.Modules;

public abstract class ModuleDefinition
{
  public abstract string Handle { get; }
  public abstract string Name { get; }
  public virtual string Icon => "folder";
  public abstract IReadOnlyList<Feature> Features { get; }
}
