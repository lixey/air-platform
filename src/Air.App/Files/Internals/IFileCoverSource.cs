namespace Platform.App.Files.Internals;

internal interface IFileCoverSource
{
  bool HasCovers { get; }
  string ParentService { get; }
  Task<IReadOnlyDictionary<long, GridCoverDto>> CoversAsync(IReadOnlyCollection<long> owners, CancellationToken cancellationToken);
}
