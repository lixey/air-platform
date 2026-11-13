namespace Platform.Api.Host.Internals;

internal enum EndpointParameterKind
{
  CancellationToken = 0,
  HttpRequest = 1,
  HttpContext = 2,
  ServiceProvider = 3,
  FormFiles = 4,
  RawJsonBody = 5,
  RouteOrBodyId = 6,
  MultipartBody = 7,
  JsonBodyOrService = 8
}
