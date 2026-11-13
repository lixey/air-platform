using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Platform.Agent.Tests.Internals;

internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
  public string EnvironmentName { get; set; } = environmentName;
  public string ApplicationName { get; set; } = "Air.Agent.Tests";
  public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
  public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
