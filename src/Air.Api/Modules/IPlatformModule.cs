namespace Platform.Api.Modules;

public interface IPlatformModule
{
  string Handle { get; }
  string Name { get; }
  string Icon => "folder";
  ModuleMenu Menu { get; }
  IReadOnlyList<Feature> Features { get; }
  void Configure(WebApplicationBuilder builder);
  void ConfigureServices(IServiceCollection services) { }
}
