namespace Platform.Api.Modules;

public abstract class PlatformModule<TDefinition> : PlatformModule
  where TDefinition : ModuleDefinition, new()
{
  private readonly TDefinition _definition = new();

  public override string Handle => _definition.Handle;
  public override string Name => _definition.Name;
  public override string Icon => _definition.Icon;
  public override IReadOnlyList<Feature> Features => _definition.Features;
}
