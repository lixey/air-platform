using System.Globalization;
using System.IO.Compression;
using Air.App.Files.Contracts;
using Air.App.Files.Internal;
using Air.App.Form.Contracts;
using Air.App.Grid;
using Air.App.Grid.AddOns.Covers.Contracts;
using Air.App.Nav;
using Air.Data;
using Air.Data.Contracts;
using Air.Data.Entities;
using Air.Data.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Air.App.Files;

public abstract class FileGridService<TFile>(PlatformDbContext db)
  : GridService<TFile, TFile>(db), IFormFilesSource, IFileCoverSource
  where TFile : BaseAppFile, new()
{
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

  protected IFileStores Stores => new DefaultFileStores(new LocalFileStorage());

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
      var upload = new GridNavUploadModel
      {
        Key = "upload",
        Limits = LimitsOf(rules),
        CanReadMetadata = true,
        Label = L["Upload"],
        Icon = UiIcon.Import,
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      };
      nav.AddStart(upload);
    }
  }

  protected override object ShapeRow(TFile row, IReadOnlyList<GridColumnDto> columns)
  {
    var baseResult = base.ShapeRow(row, columns);
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
      Accept = [.. rules.ContentTypes],
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

  public virtual async Task<GridActionDto> Upload(
    GridUploadRequest request,
    IFormFileCollection files,
    CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(files);

    var file = files["file"] ?? files.FirstOrDefault();
    if (file == null || file.Length == 0)
    {
      throw new InvalidOperationException("No file was uploaded.");
    }

    var rules = FileRulesAttribute.Of(typeof(TFile));
    if (rules.MaxBytes > 0 && file.Length > rules.MaxBytes)
    {
      throw new InvalidOperationException($"File exceeds maximum allowed size of {rules.MaxBytes} bytes.");
    }

    var normalizedType = FileTypeTable.Normalize(file.ContentType) ?? "application/octet-stream";
    var cleanedName = FileTypeTable.NameOf(file.FileName) ?? file.FileName;

    var storageKey = FileKeys.New();
    var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
    var storage = Stores.Of(storeKey);

    await using (var stream = file.OpenReadStream())
    {
      await storage.WriteAsync(storageKey, stream, cancellationToken);
    }

    string? thumbKey = null;
    var thumbFile = files["thumbnail"];
    if (thumbFile != null && thumbFile.Length > 0 && thumbFile.Length <= FileTypeTable.ThumbnailMaxBytes)
    {
      thumbKey = FileKeys.New();
      await using var thumbStream = thumbFile.OpenReadStream();
      await storage.WriteAsync(thumbKey, thumbStream, cancellationToken);
    }

    var parent = new Parent();
    SetParent(parent);
    var parentId = request.ParentId ?? ParentId;

    var entity = new TFile
    {
      Name = cleanedName,
      ContentType = normalizedType,
      Size = file.Length,
      StorageKey = storageKey,
      ThumbnailKey = thumbKey,
      Store = storeKey,
      CreatedAt = DateTimeOffset.UtcNow,
      SortOrder = 0
    };

    if (parentId.HasValue && parent.BoundProperty != null)
    {
      if (parent.BoundProperty.PropertyType == typeof(long))
      {
        parent.BoundProperty.SetValue(entity, parentId.Value);
      }
      else if (parent.BoundProperty.PropertyType == typeof(long?))
      {
        parent.BoundProperty.SetValue(entity, (long?)parentId.Value);
      }
    }

    if (entity is IHasCoverFile coverFile && rules.Cover == FileCover.Auto && parentId.HasValue && parent.BoundProperty != null)
    {
      var param = Expression.Parameter(typeof(TFile), "f");
      var propExpr = Expression.Property(param, parent.BoundProperty);
      var idExpr = Expression.Constant(parentId.Value, parent.BoundProperty.PropertyType);
      var eqExpr = Expression.Equal(propExpr, idExpr);
      var coverPropExpr = Expression.Property(param, nameof(IHasCoverFile.IsCover));
      var andExpr = Expression.AndAlso(eqExpr, coverPropExpr);
      var lambda = Expression.Lambda<Func<TFile, bool>>(andExpr, param);

      var hasCover = await db.Set<TFile>().AnyAsync(lambda, cancellationToken);
      coverFile.IsCover = !hasCover;
    }

    await db.Set<TFile>().AddAsync(entity, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);

    return new GridActionDto
    {
      Outcome = MutationOutcome.Committed,
      Id = entity.Id
    };
  }

  public virtual async Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var ids = request.SelectedIds ?? [];
    if (ids.Count == 0 && request.Id.HasValue) ids = [request.Id.Value];

    var list = await db.Set<TFile>().Where(f => ids.Contains(f.Id)).ToListAsync(cancellationToken);
    if (list.Count == 0)
    {
      return new GridDeleteDto { Outcome = MutationOutcome.Committed, Deleted = 0 };
    }

    var rules = FileRulesAttribute.Of(typeof(TFile));
    var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
    var storage = Stores.Of(storeKey);

    foreach (var item in list)
    {
      db.Set<TFile>().Remove(item);
      if (!string.IsNullOrEmpty(item.StorageKey))
      {
        try { await storage.DeleteAsync(item.StorageKey, cancellationToken); } catch { }
      }
      if (!string.IsNullOrEmpty(item.ThumbnailKey))
      {
        try { await storage.DeleteAsync(item.ThumbnailKey, cancellationToken); } catch { }
      }
    }

    await db.SaveChangesAsync(cancellationToken);

    return new GridDeleteDto
    {
      Outcome = MutationOutcome.Committed,
      Deleted = list.Count
    };
  }

  public virtual async Task<GridActionDto> SetCover(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (!HasCovers)
    {
      throw new InvalidOperationException("Entity does not support covers.");
    }

    var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault();
    if (!targetId.HasValue)
    {
      throw new InvalidOperationException("No file selected.");
    }

    var target = await db.Set<TFile>().FindAsync([targetId.Value], cancellationToken);
    if (target == null)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var parent = new Parent();
    SetParent(parent);

    if (parent.BoundProperty != null)
    {
      var parentVal = parent.BoundProperty.GetValue(target);
      if (parentVal != null)
      {
        var param = Expression.Parameter(typeof(TFile), "f");
        var propExpr = Expression.Property(param, parent.BoundProperty);
        var valExpr = Expression.Constant(parentVal, parent.BoundProperty.PropertyType);
        var eqExpr = Expression.Equal(propExpr, valExpr);
        var lambda = Expression.Lambda<Func<TFile, bool>>(eqExpr, param);

        var siblings = await db.Set<TFile>().Where(lambda).ToListAsync(cancellationToken);
        foreach (var sibling in siblings)
        {
          if (sibling is IHasCoverFile cf)
          {
            cf.IsCover = sibling.Id == targetId.Value;
          }
        }
      }
    }
    else if (target is IHasCoverFile cf)
    {
      cf.IsCover = true;
    }

    await db.SaveChangesAsync(cancellationToken);
    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  public virtual async Task<GridActionDto> Move(FileMoveRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault();
    if (!targetId.HasValue)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var target = await db.Set<TFile>().FindAsync([targetId.Value], cancellationToken);
    if (target == null)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    if (request.BeforeId.HasValue)
    {
      var before = await db.Set<TFile>().FindAsync([request.BeforeId.Value], cancellationToken);
      if (before != null)
      {
        target.SortOrder = before.SortOrder - 1;
      }
    }
    else
    {
      target.SortOrder += 1024;
    }

    await db.SaveChangesAsync(cancellationToken);
    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  public virtual async Task<FileInfoDto> GetInfo(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var id = request.Id ?? request.SelectedIds?.FirstOrDefault()
      ?? throw new InvalidOperationException("File ID required.");

    var file = await db.Set<TFile>().FindAsync([id], cancellationToken)
      ?? throw new KeyNotFoundException("File not found.");

    return new FileInfoDto
    {
      Id = file.Id,
      Name = file.Name,
      ContentType = file.ContentType,
      Size = file.Size,
      CreatedAt = file.CreatedAt,
      Description = file.Description,
      Ver = file.Ver
    };
  }

  public virtual async Task<GridActionDto> SetInfo(FileInfoRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var id = request.Id ?? request.SelectedIds?.FirstOrDefault()
      ?? throw new InvalidOperationException("File ID required.");

    var file = await db.Set<TFile>().FindAsync([id], cancellationToken)
      ?? throw new KeyNotFoundException("File not found.");

    if (!string.IsNullOrWhiteSpace(request.Name))
    {
      var ext = Path.GetExtension(file.Name);
      var stem = request.Name.Trim();
      file.Name = stem.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? stem : stem + ext;
    }

    file.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
    await db.SaveChangesAsync(cancellationToken);

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  public virtual async Task<FileMetadataDto?> FileMetadata(GridRequest request, CancellationToken cancellationToken = default)
  {
    return null;
  }

  public virtual async Task<IResult> DownloadAll(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var parent = new Parent();
    SetParent(parent);
    var parentId = request.ParentId ?? ParentId;

    IQueryable<TFile> query = db.Set<TFile>();
    if (parentId.HasValue && parent.FilterFunc != null)
    {
      query = parent.FilterFunc(query, parentId.Value);
    }

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
        if (string.IsNullOrEmpty(file.StorageKey)) continue;
        await using var fileStream = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
        if (fileStream == null) continue;

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
      CanUpload = !IsReadOnly,
      CanDelete = !IsReadOnly,
      CanSetCover = HasCovers && !IsReadOnly,
      CanReadMetadata = true
    };
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
      if (rawParent == null) continue;
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
