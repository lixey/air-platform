using Microsoft.Net.Http.Headers;

namespace Air.App.Host;

internal static class FileEndpoints
{
  public static void MapFileEndpoints(this RouteGroupBuilder api)
  {
    api.MapGet("/platform-files/{token}", async (string token, HttpContext httpContext) =>
    {
      var links = new Files.FileLinks();
      var link = links.Read(token);
      if (link == null)
      {
        return Results.NotFound();
      }

      var stores = httpContext.RequestServices.GetService<Files.IFileStores>()
        ?? new Files.DefaultFileStores(new Data.Files.LocalFileStorage());
      var storage = stores.Of(link.Store);
      var stream = await storage.OpenReadAsync(link.Key, httpContext.RequestAborted);
      if (stream == null)
      {
        return Results.NotFound();
      }

      var type = link.ContentType;
      var name = link.Name;

      if (link.IsThumbnail)
      {
        var head = new byte[Files.Internal.FileTypeTable.ThumbnailHeadBytes];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, httpContext.RequestAborted);
        if (Files.Internal.FileTypeTable.ThumbnailTypeOf(head.AsSpan(0, read)) is not { } thumbnail)
        {
          await stream.DisposeAsync();
          return Results.NotFound();
        }

        stream.Seek(0, SeekOrigin.Begin);
        type = thumbnail;
        name = Path.GetFileNameWithoutExtension(link.Name) + (thumbnail == FileType.WebP ? ".webp" : ".jpg");
      }

      var isInline = !httpContext.Request.Query.ContainsKey("download") &&
                     (link.IsThumbnail || Files.Internal.FileTypeTable.IsServedInline(type));

      var disposition = new ContentDispositionHeaderValue(isInline ? "inline" : "attachment");
      disposition.SetHttpFileName(name);
      httpContext.Response.Headers.ContentDisposition = disposition.ToString();
      httpContext.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

      return Results.Stream(stream, type, enableRangeProcessing: true);
    });
  }
}
