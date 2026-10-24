namespace Platform.Api.Host;

public interface IPlatformJob
{
  Task ExecuteAsync(CancellationToken cancellationToken);
}
