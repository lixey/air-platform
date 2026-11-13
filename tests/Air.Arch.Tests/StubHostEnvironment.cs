using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Platform.Arch.Tests;

internal sealed class StubHostEnvironment : IHostEnvironment
{
  public string EnvironmentName { get; set; } = Environments.Development;
  public string ApplicationName { get; set; } = "Air.Arch.Tests";
  public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
  public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
