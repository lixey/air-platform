namespace Air.App.Files.Internal;

public interface IFileCoverSource
{
  bool HasCovers { get; }
  string ParentService { get; }
  Task<IReadOnlyDictionary<long, GridCoverDto>> CoversAsync(IReadOnlyCollection<long> owners, CancellationToken cancellationToken);
}
