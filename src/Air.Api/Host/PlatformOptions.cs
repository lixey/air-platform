namespace Platform.Api.Host;

public sealed class PlatformOptions
{
  private readonly List<IPlatformModule> _modules = [];

  public IReadOnlyList<IPlatformModule> Modules => _modules;

  public PlatformOptions AddModule<TModule>() where TModule : IPlatformModule, new()
  {
    _modules.Add(new TModule());
    return this;
  }

  public PlatformOptions AddModule(IPlatformModule module)
  {
    ArgumentNullException.ThrowIfNull(module);
    _modules.Add(module);
    return this;
  }
}
