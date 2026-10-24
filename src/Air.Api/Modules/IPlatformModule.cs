namespace Platform.Api.Modules;

public interface IPlatformModule
{
  string Handle { get; }
  string Name { get; }
  ModuleMenu Menu { get; }
  IReadOnlyList<Feature> Features { get; }
  void Configure(WebApplicationBuilder builder);
  void ConfigureServices(IServiceCollection services) { }
}
