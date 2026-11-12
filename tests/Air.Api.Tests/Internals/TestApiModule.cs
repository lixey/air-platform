using Microsoft.AspNetCore.Builder;

namespace Platform.Api.Tests.Internals;

internal sealed class TestApiModule : IPlatformModule
{
  public string Handle => "Test";
  public string Name => "Test";
  public ModuleMenu Menu { get; } = new([]);
  public IReadOnlyList<Feature> Features => [];

  public void Configure(WebApplicationBuilder builder) { }
}
