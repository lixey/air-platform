namespace Platform.Api.Host.Internals;

internal enum EndpointParameterKind
{
  CancellationToken = 0,
  HttpRequest = 1,
  HttpContext = 2,
  ServiceProvider = 3,
  FromServices = 4,
  FormFiles = 5,
  RawJsonBody = 6,
  RouteOrBodyId = 7,
  MultipartBody = 8,
  CustomScopedRequest = 9,
  ScopedJsonBody = 10,
  JsonBody = 11
}
