namespace Platform.App.Host;

public static class PlatformAppExtensions
{
  public static WebApplicationBuilder AddPlatformModule<TModule>(this WebApplicationBuilder builder)
    where TModule : class, IPlatformModule, new() =>
    builder.AddPlatformModule(new TModule());

  public static WebApplicationBuilder AddPlatformModule(this WebApplicationBuilder builder, IPlatformModule module)
  {
    builder.AddPlatformApiModule(module);
    builder.AddPlatformService<PlatformMetadataService>();
    return builder;
  }

  public static void MapPlatformEndpoints(this WebApplication app)
  {
    _ = app.MapPlatformApiEndpoints();
  }

  public static void MapPlatform(this WebApplication app)
  {
    _ = app.MapPlatformApi();

    var webRoot = new[]
      {
        app.Environment.WebRootPath,
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot")
      }
      .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(Path.Combine(p, "index.html")));

    if (webRoot != null)
    {
      var provider = new PhysicalFileProvider(Path.GetFullPath(webRoot));
      var indexFile = Path.Combine(provider.Root, "index.html");
      app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
      app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
      app.MapFallback(context =>
      {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
          context.Response.StatusCode = StatusCodes.Status404NotFound;
          return Task.CompletedTask;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.SendFileAsync(indexFile);
      });
    }
  }
}
