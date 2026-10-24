namespace Platform.Api.Modules;

public abstract class PlatformModule<TDefinition> : PlatformModule
  where TDefinition : ModuleDefinition, new()
{
  private readonly TDefinition _definition = new();

  public override string Handle => _definition.GetHandle();
  public override string Name => _definition.GetName();
  public override IReadOnlyList<Feature> Features => _definition.GetFeatures();
}
