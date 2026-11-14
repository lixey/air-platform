using System.IO.Compression;
using Platform.Api.Files.Internals;
using Platform.App.Files.Internals;
using Platform.App.Internals;

namespace Platform.App.Files;

public abstract partial class FileGridService<TFile>(PlatformDbContext db, PlatformAppService? app = null)
  : GridService<TFile, TFile>(db, app), IFormFilesSource, IFileCoverSource
  where TFile : BaseAppFile, new()
{
  private const int OrderStep = 1024;
  private static readonly bool HasCovers = typeof(IHasCoverFile).IsAssignableFrom(typeof(TFile));
  private static readonly DateTimeOffset ZipMinTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
  private static readonly DateTimeOffset ZipMaxTimestamp = new(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);

  bool IFileCoverSource.HasCovers => HasCovers;

  string IFileCoverSource.ParentService => GetParent().BoundParentService ?? string.Empty;

  int IFormFilesSource.PageSize => 25;

  protected FileLinks Links => field ??= PlatformApp?.Services.GetService<FileLinks>() ?? new();

  protected IFileStores Stores => FileStorageCleaner.ResolveStores(PlatformApp?.Services);

  protected override void SetQuery() => Query.SortBy(r => r.SortOrder).ThenByDescending(r => r.Id);

  protected override void SetHooks() => Hooks.AllowMultiDelete();

  protected override void SetColumns()
  {
    Columns.Add(r => r.Name).Cell(new GridCellFileModel());
    if (HasCovers)
    {
      Columns.Add(r => ((IHasCoverFile)(object)r).IsCover).Label(L["Cover"]);
    }

    Columns.Add(r => r.ContentType).Label(L["Type"]);
    Columns.Add(r => r.Size).Label(L["Size"]);
    Columns.Add(r => r.CreatedAt).Label(L["Created"]);
  }

  protected override void SetNav()
  {
    base.SetNav();

    if (!IsReadOnly)
    {
      var rules = FileRulesAttribute.Of(typeof(TFile));
      var upload = new GridNavUploadModel
      {
        Key = "upload",
        Limits = LimitsOf(rules),
        CanReadMetadata = CanReadFileMetadata(),
        Label = L["Upload"],
        Icon = UiIcon.Import,
        Color = UiColor.Orange,
        Variant = ButtonVariant.Soft
      };
      Nav.AddStart(upload);
    }
  }

  public override bool CanDelete() => !IsReadOnly;

  protected override RowDto ShapeRow(
    TFile row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<TFile, bool> Predicate)>? rowFlags = null)
  {
    var shaped = base.ShapeRow(row, columns, rowFlags);
    if (Links.TryUrl(row) is { } url)
    {
      shaped.Cells["url"] = url;
    }

    if (Links.TryThumbnailUrl(row) is { } thumbUrl)
    {
      shaped.Cells["thumbUrl"] = thumbUrl;
    }

    return shaped;
  }

  public static FileUploadLimitsDto LimitsOf(FileRulesAttribute rules) =>
    new()
    {
      Accept = [.. FileTypeTable.TypesOf(rules.ContentTypes)],
      Extensions = [.. FileTypeTable.ExtensionsOf(rules.ContentTypes)],
      MaxBytes = rules.MaxBytes,
      SourceMaxBytes = rules.SourceMaxBytes > 0 ? rules.SourceMaxBytes : rules.MaxBytes,
      MaxFiles = rules.MaxFiles,
      MaxTotalBytes = rules.MaxTotalBytes,
      Images = rules.Images,
      ImageMaxSide = rules.ImageMaxSide,
      ImageQuality = rules.ImageQuality,
      MaxPixels = FileTypeTable.MaxPixels,
      ThumbnailMaxBytes = FileTypeTable.ThumbnailMaxBytes
    };

  private static int? Between(long? previous, long? next)
  {
    long? order = (previous, next) switch
    {
      (null, null) => 0,
      (null, { } after) => after - OrderStep,
      ({ } before, null) => before + OrderStep,
      ({ } before, { } after) when after - before >= 2 => (before + after) / 2,
      _ => null
    };
    return order is >= int.MinValue and <= int.MaxValue ? (int)order.Value : null;
  }

  private static void Renumber(List<TFile> ordered)
  {
    for (var i = 0; i < ordered.Count; i++)
    {
      ordered[i].SortOrder = i * OrderStep;
    }
  }

  private static DateTimeOffset ClampZipTimestamp(DateTimeOffset timestamp)
  {
    if (timestamp < ZipMinTimestamp)
    {
      return ZipMinTimestamp;
    }

    return timestamp > ZipMaxTimestamp ? ZipMaxTimestamp : timestamp;
  }

  private static Expression<Func<TFile, bool>> BuildParentFilter(PropertyInfo boundProp, object parentVal) =>
    ParentFilterExpressions.BuildParentKeyFilter<TFile>(boundProp, parentVal);

  [Endpoint]
  [RequireAction(Right.View)]
  public virtual async Task<FileInfoDto> GetInfo(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentService = request.ParentService ?? ParentService;
    ParentId = request.ParentId ?? ParentId;
    var id = request.Id ?? request.SelectedIds?.FirstOrDefault()
      ?? throw new InvalidOperationException(L["FileIdRequired"]);

    if (!await EnsureParentScopeAsync(lockParent: false, cancellationToken))
    {
      throw new KeyNotFoundException(L["FileNotFound"]);
    }

    var file = await RestrictedQuery().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
      ?? throw new KeyNotFoundException(L["FileNotFound"]);

    string? uploadedBy = null;
    if (file.UploadedById is > 0 and var uid)
    {
      var users = await db.LoadUserNamesAsync([uid], cancellationToken);
      users.TryGetValue(uid, out uploadedBy);
    }

    return new FileInfoDto
    {
      Id = file.Id,
      Name = file.Name,
      ContentType = file.ContentType,
      Size = file.Size,
      Width = file.Width,
      Height = file.Height,
      CreatedAt = file.CreatedAt,
      UploadedBy = uploadedBy,
      Description = file.Description,
      Ver = file.Ver
    };
  }

  [Endpoint]
  [RequireAction("FileMetadata", fallbackToView: true)]
  public virtual async Task<FileMetadataDto?> FileMetadata(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentService = request.ParentService ?? ParentService;
    ParentId = request.ParentId ?? ParentId;
    var id = request.Id ?? request.SelectedIds?.FirstOrDefault()
      ?? throw new InvalidOperationException(L["FileIdRequired"]);

    if (!await EnsureParentScopeAsync(lockParent: false, cancellationToken))
    {
      throw new KeyNotFoundException(L["FileNotFound"]);
    }

    var file = await RestrictedQuery().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
      ?? throw new KeyNotFoundException(L["FileNotFound"]);

    if (string.IsNullOrWhiteSpace(file.Metadata))
    {
      return null;
    }

    try
    {
      return System.Text.Json.JsonSerializer.Deserialize<FileMetadataDto>(
        file.Metadata,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
    catch
    {
      return null;
    }
  }

  [Endpoint(ResponseType = typeof(Stream))]
  [RequireAction(Right.View)]
  public virtual async Task<IResult> DownloadAll(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var parent = GetParent();
    var parentId = request.ParentId ?? ParentId;
    ParentService = request.ParentService ?? ParentService;
    ParentId = parentId;

    if (parent.BoundProperty != null && (!parentId.HasValue || parentId.Value <= 0))
    {
      throw new InvalidOperationException(L["ParentIdRequired"]);
    }

    if (!await EnsureParentScopeAsync(lockParent: false, cancellationToken))
    {
      throw new KeyNotFoundException(L["FileNotFound"]);
    }

    var query = RestrictedQuery();

    var files = await query.OrderBy(f => f.SortOrder).ThenByDescending(f => f.Id).ToListAsync(cancellationToken);
    var rules = FileRulesAttribute.Of(typeof(TFile));
    var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;

    var memoryStream = new MemoryStream();
    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
    {
      var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      foreach (var file in files)
      {
        if (string.IsNullOrEmpty(file.StorageKey))
        {
          continue;
        }

        var storage = Stores.Of(file.Store ?? storeKey);
        await using var fileStream = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
        if (fileStream == null)
        {
          continue;
        }

        var entryName = Path.GetFileName((file.Name ?? string.Empty).Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(entryName))
        {
          entryName = $"file_{file.Id}";
        }

        if (seenNames.TryGetValue(entryName, out var count))
        {
          seenNames[entryName] = count + 1;
          var ext = Path.GetExtension(entryName);
          var baseName = Path.GetFileNameWithoutExtension(entryName);
          entryName = $"{baseName} ({count + 1}){ext}";
        }
        else
        {
          seenNames[entryName] = 1;
        }

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = ClampZipTimestamp(file.CreatedAt);
        await using var entryStream = entry.Open();
        await fileStream.CopyToAsync(entryStream, cancellationToken);
      }
    }

    memoryStream.Seek(0, SeekOrigin.Begin);
    var fileName = $"files_{parentId ?? 0}.zip";
    return Results.File(memoryStream, "application/zip", fileName);
  }

  Task<FormFilesModel> IFormFilesSource.BuildFormFilesAsync(
    string key,
    string? label,
    string? tab,
    string parentService,
    long? parentId,
    string look,
    CancellationToken cancellationToken
  )
  {
    ParentService = parentService;
    ParentId = parentId;
    var rules = FileRulesAttribute.Of(typeof(TFile));
    var canEdit = !IsReadOnly;
    return Task.FromResult(new FormFilesModel
    {
      Key = key,
      Label = label,
      Tab = tab,
      Service = ServicePathResolver.Resolve(GetType()),
      ParentService = parentService,
      ParentId = parentId,
      Look = look,
      Limits = LimitsOf(rules),
      CanUpload = canEdit,
      CanDelete = canEdit,
      CanSetCover = HasCovers && canEdit,
      CanReadMetadata = CanReadFileMetadata()
    });
  }

  private bool CanReadFileMetadata() =>
    Feature.TryGetRight("FileMetadata", out var right) &&
    User is { IsAuthenticated: true } &&
    (User.Can(right) ||
     User.Can(Feature.All) ||
     User.Can("*"));

  async Task<long?> IFormFilesSource.FindExistingParentIdByHashAsync(
    string sha256,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(sha256))
    {
      return null;
    }

    var parent = GetParent();
    if (parent.BoundProperty == null)
    {
      return null;
    }

    var existing = await UnscopedRestrictedQuery()
      .AsNoTracking()
      .Where(f => f.Sha256 == sha256)
      .OrderByDescending(f => f.Id)
      .FirstOrDefaultAsync(cancellationToken);

    if (existing == null || parent.BoundProperty.GetValue(existing) is not { } rawParent)
    {
      return null;
    }

    var parentKey = Convert.ToInt64(rawParent, CultureInfo.InvariantCulture);
    return parentKey > 0 ? parentKey : null;
  }

  async Task<IReadOnlyDictionary<long, GridCoverDto>> IFileCoverSource.CoversAsync(
    IReadOnlyCollection<long> owners,
    CancellationToken cancellationToken
  )
  {
    if (!HasCovers || owners.Count == 0)
    {
      return new Dictionary<long, GridCoverDto>();
    }

    var parent = GetParent();
    if (parent.BoundProperty == null)
    {
      return new Dictionary<long, GridCoverDto>();
    }

    var inOwnersLambda = ParentFilterExpressions.BuildKeyInFilter<TFile>(parent.BoundProperty, owners);
    var param = inOwnersLambda.Parameters[0];
    var isCoverExpr = Expression.Equal(
      Expression.Property(param, nameof(IHasCoverFile.IsCover)),
      Expression.Constant(true));
    var lambda = Expression.Lambda<Func<TFile, bool>>(Expression.AndAlso(isCoverExpr, inOwnersLambda.Body), param);

    var covers = await UnscopedRestrictedQuery().AsNoTracking().Where(lambda).ToListAsync(cancellationToken);
    var result = new Dictionary<long, GridCoverDto>();

    foreach (var cover in covers)
    {
      var rawParent = parent.BoundProperty.GetValue(cover);
      if (rawParent == null)
      {
        continue;
      }

      var parentKey = Convert.ToInt64(rawParent, CultureInfo.InvariantCulture);

      if (!result.ContainsKey(parentKey) && Links.TryUrl(cover) is { } url)
      {
        result[parentKey] = new GridCoverDto
        {
          Id = cover.Id,
          Name = cover.Name,
          ContentType = cover.ContentType,
          Url = url,
          ThumbUrl = Links.TryThumbnailUrl(cover)
        };
      }
    }

    return result;
  }
}
