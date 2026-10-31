using Platform.Api.Files.Internals;
using Platform.App.Files.Internals;

namespace Platform.App.Files;

public abstract partial class FileGridService<TFile>
  where TFile : BaseAppFile, new()
{
  [RequireAction(EndpointAction.Edit)]
  public virtual async Task<GridActionDto> Upload(
    GridUploadRequest request,
    IFormFileCollection files,
    CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(files);

    ParentService = request.ParentService ?? ParentService;
    ParentId = request.ParentId ?? ParentId;
    if (IsReadOnly)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var file = files["file"] ?? files.FirstOrDefault();
    if (file == null || file.Length == 0)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FileCountRefused"], Code = "file.count", Type = MessageType.Error }]
      };
    }

    if (FileTypeTable.NameOf(file.FileName) is not { } cleanedName)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FileNameRefused"], Code = "file.name", Type = MessageType.Error }]
      };
    }

    var rules = FileRulesAttribute.Of(typeof(TFile));
    if (rules.MaxBytes > 0 && file.Length > rules.MaxBytes)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FileTooLarge", Math.Round(rules.MaxBytes / (1024d * 1024d), 1)], Code = "file.size", Type = MessageType.Error }]
      };
    }

    var allowedTypes = FileTypeTable.TypesOf(rules.ContentTypes)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var normalizedType = FileTypeTable.Normalize(file.ContentType) ?? "application/octet-stream";
    if (allowedTypes.Count > 0 && !allowedTypes.Contains(normalizedType))
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FileTypeRefused", string.Join(", ", allowedTypes)], Code = "file.type", Type = MessageType.Error }]
      };
    }

    if (allowedTypes.Count > 0 && !FileTypeTable.HasExtension(normalizedType, cleanedName))
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FileExtensionRefused", normalizedType], Code = "file.extension", Type = MessageType.Error }]
      };
    }

    byte[] fileBytes;
    await using (var readStream = file.OpenReadStream())
    {
      using var ms = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
      await readStream.CopyToAsync(ms, cancellationToken);
      fileBytes = ms.ToArray();
    }

    var headSpan = fileBytes.AsSpan(0, Math.Min(fileBytes.Length, 12));
    if (!FileTypeTable.StartsAsDeclared(normalizedType, headSpan) ||
        FileTypeTable.IsZipDocument(normalizedType) && !FileTypeTable.StartsAsZip(headSpan))
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FileContentRefused", normalizedType], Code = "file.content", Type = MessageType.Error }]
      };
    }

    var sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fileBytes));

    string? normalizedMetadata = null;
    if (!string.IsNullOrWhiteSpace(request.Metadata))
    {
      if (request.Metadata.Length > 16384)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FileMetadataInvalid"], Path = "metadata", Code = "file.metadata", Type = MessageType.Error }]
        };
      }

      try
      {
        var parsedMeta = System.Text.Json.JsonSerializer.Deserialize<FileMetadataDto>(
          request.Metadata,
          new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (parsedMeta != null)
        {
          normalizedMetadata = System.Text.Json.JsonSerializer.Serialize(
            parsedMeta,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }
      }
      catch
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FileMetadataInvalid"], Path = "metadata", Code = "file.metadata", Type = MessageType.Error }]
        };
      }
    }

    var parent = GetParent();
    var parentId = request.ParentId ?? ParentId;
    ParentId = parentId;

    return await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused };
      }

      List<TFile>? siblings = null;
      if (parentId.HasValue && parent.BoundProperty != null)
      {
        siblings = await RestrictedQuery()
          .AsTracking()
          .OrderBy(f => f.SortOrder)
          .ThenByDescending(f => f.Id)
          .ToListAsync(mutationCt);

        if (rules.MaxFiles > 0 && siblings.Count >= rules.MaxFiles)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = [new MessageDto { Detail = L["FileCountLimit", rules.MaxFiles], Code = "file.limit", Type = MessageType.Error }]
          };
        }

        if (rules.MaxTotalBytes > 0 && siblings.Sum(s => s.Size) + file.Length > rules.MaxTotalBytes)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = [new MessageDto { Detail = L["FileTotalTooLarge", Math.Round(rules.MaxTotalBytes / (1024d * 1024d), 1)], Code = "file.limit", Type = MessageType.Error }]
          };
        }

        if (siblings.FirstOrDefault(s => string.Equals(s.Sha256, sha256, StringComparison.OrdinalIgnoreCase)) is { } duplicate)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = [new MessageDto { Detail = L["FileDuplicate", duplicate.Name], Code = "file.duplicate", Type = MessageType.Error }]
          };
        }
      }

      var storageKey = FileKeys.New();
      var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
      var storage = Stores.Of(storeKey);

      await using (var stream = new MemoryStream(fileBytes, writable: false))
      {
        await storage.WriteAsync(storageKey, stream, mutationCt);
      }

      string? thumbKey = null;
      var thumbFile = files["thumbnail"];
      if (thumbFile != null && thumbFile.Length > 0 && thumbFile.Length <= FileTypeTable.ThumbnailMaxBytes)
      {
        thumbKey = FileKeys.New();
        await using var thumbStream = thumbFile.OpenReadStream();
        await storage.WriteAsync(thumbKey, thumbStream, mutationCt);
      }

      var entity = new TFile
      {
        Name = cleanedName,
        ContentType = normalizedType,
        Size = file.Length,
        StorageKey = storageKey,
        ThumbnailKey = thumbKey,
        Store = storeKey,
        Sha256 = sha256,
        Metadata = normalizedMetadata,
        UploadedById = User?.UserId,
        CreatedAt = DateTimeOffset.UtcNow,
        SortOrder = 0
      };

      if (parentId.HasValue && parent.BoundProperty != null && siblings != null)
      {
        if (parent.BoundProperty.PropertyType == typeof(long))
        {
          parent.BoundProperty.SetValue(entity, parentId.Value);
        }
        else if (parent.BoundProperty.PropertyType == typeof(long?))
        {
          parent.BoundProperty.SetValue(entity, (long?)parentId.Value);
        }

        var place = request.AfterId is { } afterId && siblings.FindIndex(f => f.Id == afterId) is >= 0 and var idx
          ? idx + 1
          : siblings.Count;

        if (Between(place > 0 ? siblings[place - 1].SortOrder : null, place < siblings.Count ? siblings[place].SortOrder : null) is { } order)
        {
          entity.SortOrder = order;
        }
        else
        {
          siblings.Insert(place, entity);
          Renumber(siblings);
        }

        if (entity is IHasCoverFile coverFile && rules.Cover == FileCover.Auto)
        {
          coverFile.IsCover = !siblings.OfType<IHasCoverFile>().Any(s => !ReferenceEquals(s, entity) && s.IsCover);
        }
      }

      await db.Set<TFile>().AddAsync(entity, mutationCt);
      await db.SaveChangesAsync(mutationCt);

      await AfterUploadAsync(entity, fileBytes, mutationCt);
      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(mutationCt);
      }

      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        Id = entity.Id
      };
    }, ct: cancellationToken);
  }

  protected virtual Task AfterUploadAsync(TFile entity, byte[] content, CancellationToken cancellationToken) =>
    Task.CompletedTask;

  [RequireAction(EndpointAction.Edit)]
  public override Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    DeleteAsync(request, cancellationToken);

  public override async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentService = request.ParentService ?? ParentService;
    ParentId = request.ParentId ?? ParentId;

    var rules = FileRulesAttribute.Of(typeof(TFile));
    var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
    var storage = Stores.Of(storeKey);
    var keysToDelete = new List<string>();

    var result = await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
      {
        return new GridDeleteDto { Outcome = MutationOutcome.Refused, Deleted = 0 };
      }

      var ids = request.SelectedIds ?? [];
      if (ids.Count == 0 && request.Id.HasValue)
      {
        ids = [request.Id.Value];
      }

      var list = await RestrictedQuery().AsTracking().Where(f => ids.Contains(f.Id)).ToListAsync(mutationCt);
      if (list.Count == 0)
      {
        return new GridDeleteDto { Outcome = MutationOutcome.Committed, Deleted = 0 };
      }

      var parent = GetParent();
      var parentsLosingCover = new HashSet<object>();

      foreach (var item in list)
      {
        if (HasCovers && rules.Cover == FileCover.Auto && parent.BoundProperty != null &&
            item is IHasCoverFile { IsCover: true } &&
            parent.BoundProperty.GetValue(item) is { } parentVal)
        {
          parentsLosingCover.Add(parentVal);
        }

        if (!string.IsNullOrEmpty(item.StorageKey))
        {
          keysToDelete.Add(item.StorageKey);
        }

        if (!string.IsNullOrEmpty(item.ThumbnailKey))
        {
          keysToDelete.Add(item.ThumbnailKey);
        }

        db.Set<TFile>().Remove(item);
      }

      await db.SaveChangesAsync(mutationCt);

      if (parentsLosingCover.Count > 0 && parent.BoundProperty != null)
      {
        foreach (var parentVal in parentsLosingCover)
        {
          var lambda = BuildParentFilter(parent.BoundProperty, parentVal);
          var nextCover = await db.Set<TFile>()
            .AsTracking()
            .Where(lambda)
            .OrderBy(f => f.SortOrder)
            .ThenByDescending(f => f.Id)
            .FirstOrDefaultAsync(mutationCt);

          if (nextCover is IHasCoverFile cf)
          {
            cf.IsCover = true;
          }
        }

        await db.SaveChangesAsync(mutationCt);
      }

      return new GridDeleteDto
      {
        Outcome = MutationOutcome.Committed,
        Deleted = list.Count
      };
    }, ct: cancellationToken);

    if (result.Outcome == MutationOutcome.Committed && keysToDelete.Count > 0)
    {
      var distinctKeys = keysToDelete.Distinct(StringComparer.Ordinal).ToList();
      var referencedKeys = (await db.Set<TFile>()
        .AsNoTracking()
        .Where(f => distinctKeys.Contains(f.StorageKey) || f.ThumbnailKey != null && distinctKeys.Contains(f.ThumbnailKey))
        .Select(f => new { f.StorageKey, f.ThumbnailKey })
        .ToListAsync(cancellationToken))
        .SelectMany(f => new[] { f.StorageKey, f.ThumbnailKey })
        .Where(k => !string.IsNullOrEmpty(k))
        .ToHashSet(StringComparer.Ordinal);

      foreach (var key in distinctKeys)
      {
        if (referencedKeys.Contains(key))
        {
          continue;
        }

        try
        {
          await storage.DeleteAsync(key, cancellationToken);
        }
        catch { }
      }
    }

    return result;
  }

  [RequireAction(EndpointAction.Edit)]
  public virtual async Task<GridActionDto> SetCover(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentService = request.ParentService ?? ParentService;
    ParentId = request.ParentId ?? ParentId;

    return await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly || !HasCovers)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused };
      }

      var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault();
      if (!targetId.HasValue)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused };
      }

      var target = await RestrictedQuery().AsTracking().FirstOrDefaultAsync(f => f.Id == targetId.Value, mutationCt);
      if (target == null)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused };
      }

      var parent = GetParent();

      if (parent.BoundProperty != null)
      {
        var parentVal = parent.BoundProperty.GetValue(target);
        if (parentVal != null)
        {
          var lambda = BuildParentFilter(parent.BoundProperty, parentVal);
          var siblings = await db.Set<TFile>().AsTracking().Where(lambda).ToListAsync(mutationCt);
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

      await db.SaveChangesAsync(mutationCt);
      return new GridActionDto { Outcome = MutationOutcome.Committed };
    }, ct: cancellationToken);
  }

  [RequireAction(EndpointAction.Edit)]
  public virtual async Task<GridActionDto> Move(FileMoveRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentService = request.ParentService ?? ParentService;
    ParentId = request.ParentId ?? ParentId;

    return await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused };
      }

      var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault();
      if (!targetId.HasValue)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused };
      }

      var target = await RestrictedQuery().AsTracking().FirstOrDefaultAsync(f => f.Id == targetId.Value, mutationCt);
      if (target == null)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused };
      }

      if (request.BeforeId == target.Id)
      {
        return new GridActionDto { Outcome = MutationOutcome.Committed };
      }

      var parent = GetParent();

      var siblingsQuery = RestrictedQuery().AsTracking().Where(f => f.Id != target.Id);
      if (parent.BoundProperty != null && parent.BoundProperty.GetValue(target) is { } parentVal)
      {
        siblingsQuery = siblingsQuery.Where(BuildParentFilter(parent.BoundProperty, parentVal));
      }

      var others = await siblingsQuery
        .OrderBy(f => f.SortOrder)
        .ThenByDescending(f => f.Id)
        .ToListAsync(mutationCt);

      var place = request.BeforeId is { } beforeId && others.FindIndex(f => f.Id == beforeId) is >= 0 and var idx
        ? idx
        : others.Count;

      if (Between(place > 0 ? others[place - 1].SortOrder : null, place < others.Count ? others[place].SortOrder : null) is { } order)
      {
        target.SortOrder = order;
      }
      else
      {
        others.Insert(place, target);
        Renumber(others);
      }

      await db.SaveChangesAsync(mutationCt);
      return new GridActionDto { Outcome = MutationOutcome.Committed };
    }, ct: cancellationToken);
  }

  [RequireAction(EndpointAction.Edit)]
  public virtual async Task<GridActionDto> SetInfo(FileInfoRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentService = request.ParentService ?? ParentService;
    ParentId = request.ParentId ?? ParentId;

    return await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused };
      }

      var stem = request.Name?.Trim() ?? string.Empty;
      if (stem.Length == 0 || stem.Any(c => c is '/' or '\\' || char.IsControl(c)))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FileNameInvalid"], Path = "name", Code = "file.name", Type = MessageType.Error }]
        };
      }

      var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
      if (description?.Length > BaseAppFile.DescriptionMaxLength)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FileDescriptionTooLong"], Path = "description", Code = "form.validation.maxLength", Type = MessageType.Error }]
        };
      }

      var id = request.Id ?? request.SelectedIds?.FirstOrDefault()
        ?? throw new InvalidOperationException("File ID required.");

      var file = await RestrictedQuery().AsTracking().FirstOrDefaultAsync(f => f.Id == id, mutationCt)
        ?? throw new KeyNotFoundException("File not found.");

      var ext = Path.GetExtension(file.Name);
      var newName = stem.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? stem : stem + ext;
      if (newName.Length > BaseAppFile.NameMaxLength)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FileNameRefused"], Path = "name", Code = "file.name", Type = MessageType.Error }]
        };
      }

      file.Name = newName;
      file.Description = description;
      await db.SaveChangesAsync(mutationCt);

      return new GridActionDto { Outcome = MutationOutcome.Committed };
    }, ct: cancellationToken);
  }

  async Task<long?> IFormFilesSource.AttachFileAsync(
    long parentId,
    string fileName,
    string contentType,
    byte[] content,
    CancellationToken cancellationToken
  )
  {
    if (parentId <= 0 || content.Length == 0)
    {
      return null;
    }

    var parent = GetParent();
    ParentId = parentId;
    ParentService ??= parent.BoundParentService;

    if (FileTypeTable.NameOf(fileName) is not { } cleanedName)
    {
      return null;
    }

    var rules = FileRulesAttribute.Of(typeof(TFile));
    if (rules.MaxBytes > 0 && content.Length > rules.MaxBytes)
    {
      return null;
    }

    var allowedTypes = FileTypeTable.TypesOf(rules.ContentTypes)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var normalizedType = FileTypeTable.Normalize(contentType) ?? "application/octet-stream";
    if (allowedTypes.Count > 0 &&
        (!allowedTypes.Contains(normalizedType) || !FileTypeTable.HasExtension(normalizedType, cleanedName)))
    {
      return null;
    }

    var headSpan = content.AsSpan(0, Math.Min(content.Length, 12));
    if (!FileTypeTable.StartsAsDeclared(normalizedType, headSpan) ||
        FileTypeTable.IsZipDocument(normalizedType) && !FileTypeTable.StartsAsZip(headSpan))
    {
      return null;
    }

    var sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

    return await db.RunMutationAsync<long?>(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
      {
        return null;
      }

      List<TFile>? siblings = null;
      if (parent.BoundProperty != null)
      {
        siblings = await RestrictedQuery()
          .AsTracking()
          .OrderBy(f => f.SortOrder)
          .ThenByDescending(f => f.Id)
          .ToListAsync(mutationCt);

        if (siblings.FirstOrDefault(s => string.Equals(s.Sha256, sha256, StringComparison.OrdinalIgnoreCase)) is { } duplicate)
        {
          return duplicate.Id;
        }

        if (rules.MaxFiles > 0 && siblings.Count >= rules.MaxFiles)
        {
          return null;
        }

        if (rules.MaxTotalBytes > 0 && siblings.Sum(s => s.Size) + content.Length > rules.MaxTotalBytes)
        {
          return null;
        }
      }

      var storageKey = FileKeys.New();
      var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
      var storage = Stores.Of(storeKey);

      await using (var stream = new MemoryStream(content, writable: false))
      {
        await storage.WriteAsync(storageKey, stream, mutationCt);
      }

      var entity = new TFile
      {
        Name = cleanedName,
        ContentType = normalizedType,
        Size = content.Length,
        StorageKey = storageKey,
        Store = storeKey,
        Sha256 = sha256,
        UploadedById = User?.UserId,
        CreatedAt = DateTimeOffset.UtcNow,
        SortOrder = 0
      };

      if (parent.BoundProperty != null && siblings != null)
      {
        if (parent.BoundProperty.PropertyType == typeof(long))
        {
          parent.BoundProperty.SetValue(entity, parentId);
        }
        else if (parent.BoundProperty.PropertyType == typeof(long?))
        {
          parent.BoundProperty.SetValue(entity, (long?)parentId);
        }

        var place = siblings.Count;
        if (Between(place > 0 ? siblings[place - 1].SortOrder : null, null) is { } order)
        {
          entity.SortOrder = order;
        }
        else
        {
          siblings.Insert(place, entity);
          Renumber(siblings);
        }

        if (entity is IHasCoverFile coverFile && rules.Cover == FileCover.Auto)
        {
          coverFile.IsCover = !siblings.OfType<IHasCoverFile>().Any(s => !ReferenceEquals(s, entity) && s.IsCover);
        }
      }

      await db.Set<TFile>().AddAsync(entity, mutationCt);
      await db.SaveChangesAsync(mutationCt);

      await AfterUploadAsync(entity, content, mutationCt);
      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(mutationCt);
      }

      return entity.Id;
    }, ct: cancellationToken);
  }
}
