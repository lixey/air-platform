namespace Platform.Api.Contracts;

public interface IMultipartServiceRequest
{
  void BindMultipart(HttpRequest request, IFormCollection form);
}
