namespace Platform.App.Files.Internals;

public interface IFileCoverSource
{
  bool HasCovers { get; }
  string ParentService { get; }
  Task<IReadOnlyDictionary<long, GridCoverDto>> CoversAsync(IReadOnlyCollection<long> owners, CancellationToken cancellationToken);
}
