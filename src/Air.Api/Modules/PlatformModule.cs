namespace Platform.Api.Modules;

public abstract class PlatformModule : IPlatformModule
{
  public virtual string Handle => GetType().Name.Replace("Module", "");
  public virtual string Name => Handle;
  public virtual ModuleMenu Menu => ModuleMenu.Empty;
  public virtual IReadOnlyList<Feature> Features => [];

  public virtual void Configure(WebApplicationBuilder builder)
  {
    ConfigureServices(builder);
    ConfigureServices(builder.Services);
  }

  protected virtual void ConfigureServices(WebApplicationBuilder builder)
  {
  }

  public virtual void ConfigureServices(IServiceCollection services)
  {
  }
}
