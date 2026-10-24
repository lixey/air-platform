using System.IO.Compression;

namespace Platform.App.Files;

public abstract partial class FileGridService<TFile>(PlatformDbContext db, PlatformAppService? app = null)
  : GridService<TFile, TFile>(db, app), IFormFilesSource, IFileCoverSource
  where TFile : BaseAppFile, new()
{
  private const int OrderStep = 1024;
  private static readonly bool HasCovers = typeof(IHasCoverFile).IsAssignableFrom(typeof(TFile));

  bool IFileCoverSource.HasCovers => HasCovers;

  string IFileCoverSource.ParentService
  {
    get
    {
      var parent = new Parent();
      SetParent(parent);
      return parent.BoundParentService ?? string.Empty;
    }
  }

  int IFormFilesSource.PageSize => 25;

  protected FileLinks Links => new();

  protected IFileStores Stores => PlatformApp?.Services.GetService<IFileStores>() ?? new DefaultFileStores(new LocalFileStorage());

  protected override void SetQuery(Query query) => query.SortBy(r => r.SortOrder).ThenByDescending(r => r.Id);

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Name).Cell(new GridCellFileModel());
    if (HasCovers)
    {
      columns.Add(r => ((IHasCoverFile)(object)r).IsCover).Label(L["Cover"]);
    }

    columns.Add(r => r.ContentType).Label(L["Type"]);
    columns.Add(r => r.Size).Label(L["Size"]);
    columns.Add(r => r.CreatedAt).Label(L["Created"]);
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);

    if (!IsReadOnly)
    {
      var rules = FileRulesAttribute.Of(typeof(TFile));
      var canReadMeta = User == null || !User.IsAuthenticated || User.Can(Feature.Action("FileMetadata")) || User.Can(Feature.All);
      var upload = new GridNavUploadModel
      {
        Key = "upload",
        Limits = LimitsOf(rules),
        CanReadMetadata = canReadMeta,
        Label = L["Upload"],
        Icon = UiIcon.Import,
        Color = UiColor.Orange,
        Variant = ButtonVariant.Soft
      };
      nav.AddStart(upload);
    }
  }

  public override bool CanDelete() => !IsReadOnly;

  protected override object ShapeRow(
    TFile row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<TFile, bool> Predicate)>? rowFlags = null)
  {
    var baseResult = base.ShapeRow(row, columns, rowFlags);
    if (baseResult is not Dictionary<string, object?> dict)
    {
      return baseResult;
    }

    if (Links.TryUrl(row) is { } url)
    {
      dict["url"] = url;
    }

    if (Links.TryThumbnailUrl(row) is { } thumbUrl)
    {
      dict["thumbUrl"] = thumbUrl;
    }

    return dict;
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

  private static Expression<Func<TFile, bool>> BuildParentFilter(System.Reflection.PropertyInfo boundProp, object parentVal)
  {
    var param = Expression.Parameter(typeof(TFile), "f");
    var propExpr = Expression.Property(param, boundProp);
    var valExpr = Expression.Constant(parentVal, boundProp.PropertyType);
    return Expression.Lambda<Func<TFile, bool>>(Expression.Equal(propExpr, valExpr), param);
  }

  [RequireAction(EndpointAction.View)]
  public virtual async Task<FileInfoDto> GetInfo(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentId = request.ParentId ?? ParentId;
    var id = request.Id ?? request.SelectedIds?.FirstOrDefault()
      ?? throw new InvalidOperationException("File ID required.");

    var file = await RestrictedQuery().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
      ?? throw new KeyNotFoundException("File not found.");

    string? uploadedBy = null;
    if (file.UploadedById is { } uid)
    {
      var userType = db.Model.GetEntityTypes()
        .Select(t => t.ClrType)
        .FirstOrDefault(t => typeof(BaseAppUser).IsAssignableFrom(t) && !t.IsAbstract);
      if (userType != null)
      {
        try
        {
          if (await db.FindAsync(userType, [uid], cancellationToken) is BaseAppUser u)
          {
            uploadedBy = !string.IsNullOrWhiteSpace(u.Name) ? u.Name : u.Login;
          }
        }
        catch { }
      }
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

  [RequireAction("FileMetadata", fallbackToView: true)]
  public virtual async Task<FileMetadataDto?> FileMetadata(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentId = request.ParentId ?? ParentId;
    var id = request.Id ?? request.SelectedIds?.FirstOrDefault()
      ?? throw new InvalidOperationException("File ID required.");

    var file = await RestrictedQuery().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
      ?? throw new KeyNotFoundException("File not found.");

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

  [RequireAction(EndpointAction.View)]
  public virtual async Task<IResult> DownloadAll(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var parent = new Parent();
    SetParent(parent);
    var parentId = request.ParentId ?? ParentId;
    ParentId = parentId;

    if (parent.BoundProperty != null && (!parentId.HasValue || parentId.Value <= 0))
    {
      throw new InvalidOperationException("Parent ID required.");
    }

    var query = RestrictedQuery();

    var files = await query.OrderBy(f => f.SortOrder).ThenByDescending(f => f.Id).ToListAsync(cancellationToken);
    var rules = FileRulesAttribute.Of(typeof(TFile));
    var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
    var storage = Stores.Of(storeKey);

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

        await using var fileStream = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
        if (fileStream == null)
        {
          continue;
        }

        var entryName = file.Name;
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
        entry.LastWriteTime = file.CreatedAt;
        await using var entryStream = entry.Open();
        await fileStream.CopyToAsync(entryStream, cancellationToken);
      }
    }

    memoryStream.Seek(0, SeekOrigin.Begin);
    var fileName = $"files_{parentId ?? 0}.zip";
    return Results.File(memoryStream, "application/zip", fileName);
  }

  async Task<FormFilesModel> IFormFilesSource.BuildFormFilesAsync(
    string key,
    string? label,
    string? tab,
    string parentService,
    long? parentId,
    string look,
    CancellationToken cancellationToken
  )
  {
    var rules = FileRulesAttribute.Of(typeof(TFile));
    var canEdit = !IsReadOnly;
    var canReadMeta = User == null || !User.IsAuthenticated || User.Can(Feature.Action("FileMetadata")) || User.Can(Feature.All);
    return new FormFilesModel
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
      CanReadMetadata = canReadMeta
    };
  }

  async Task<long?> IFormFilesSource.FindExistingParentIdByHashAsync(
    string sha256,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(sha256))
    {
      return null;
    }

    var parent = new Parent();
    SetParent(parent);
    if (parent.BoundProperty == null)
    {
      return null;
    }

    var existing = await db.Set<TFile>()
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

    var parent = new Parent();
    SetParent(parent);
    if (parent.BoundProperty == null)
    {
      return new Dictionary<long, GridCoverDto>();
    }

    var param = Expression.Parameter(typeof(TFile), "f");
    var propExpr = Expression.Property(param, parent.BoundProperty);
    var isCoverExpr = Expression.Property(param, nameof(IHasCoverFile.IsCover));

    // f.IsCover == true
    var coverCheck = Expression.Equal(isCoverExpr, Expression.Constant(true));

    // parentId in owners
    var ownerLongs = owners.ToList();
    var containsMethod = typeof(Enumerable).GetMethods()
      .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
      .MakeGenericMethod(typeof(long));

    Expression parentLongExpr = propExpr;
    if (parent.BoundProperty.PropertyType == typeof(long?))
    {
      parentLongExpr = Expression.Coalesce(propExpr, Expression.Constant(0L));
    }

    var inOwnersExpr = Expression.Call(containsMethod, Expression.Constant(ownerLongs), parentLongExpr);
    var combinedExpr = Expression.AndAlso(coverCheck, inOwnersExpr);
    var lambda = Expression.Lambda<Func<TFile, bool>>(combinedExpr, param);

    var covers = await db.Set<TFile>().AsNoTracking().Where(lambda).ToListAsync(cancellationToken);
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
