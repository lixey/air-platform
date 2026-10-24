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

    List<TFile>? siblings = null;
    if (parentId.HasValue && parent.BoundProperty != null)
    {
      siblings = await RestrictedQuery()
        .OrderBy(f => f.SortOrder)
        .ThenByDescending(f => f.Id)
        .ToListAsync(cancellationToken);

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

    await db.Set<TFile>().AddAsync(entity, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);

    await AfterUploadAsync(entity, fileBytes, cancellationToken);
    if (db.ChangeTracker.HasChanges())
    {
      await db.SaveChangesAsync(cancellationToken);
    }

    return new GridActionDto
    {
      Outcome = MutationOutcome.Committed,
      Id = entity.Id
    };
  }

  protected virtual Task AfterUploadAsync(TFile entity, byte[] content, CancellationToken cancellationToken) =>
    Task.CompletedTask;

  [RequireAction(EndpointAction.Edit)]
  public override Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    DeleteAsync(request, cancellationToken);

  public override async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentId = request.ParentId ?? ParentId;
    if (IsReadOnly)
    {
      return new GridDeleteDto { Outcome = MutationOutcome.Refused, Deleted = 0 };
    }

    var ids = request.SelectedIds ?? [];
    if (ids.Count == 0 && request.Id.HasValue)
    {
      ids = [request.Id.Value];
    }

    var list = await RestrictedQuery().Where(f => ids.Contains(f.Id)).ToListAsync(cancellationToken);
    if (list.Count == 0)
    {
      return new GridDeleteDto { Outcome = MutationOutcome.Committed, Deleted = 0 };
    }

    var rules = FileRulesAttribute.Of(typeof(TFile));
    var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
    var storage = Stores.Of(storeKey);

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

      db.Set<TFile>().Remove(item);
      if (!string.IsNullOrEmpty(item.StorageKey))
      {
        try
        {
          await storage.DeleteAsync(item.StorageKey, cancellationToken);
        }
        catch { }
      }

      if (!string.IsNullOrEmpty(item.ThumbnailKey))
      {
        try
        {
          await storage.DeleteAsync(item.ThumbnailKey, cancellationToken);
        }
        catch { }
      }
    }

    await db.SaveChangesAsync(cancellationToken);

    if (parentsLosingCover.Count > 0 && parent.BoundProperty != null)
    {
      foreach (var parentVal in parentsLosingCover)
      {
        var lambda = BuildParentFilter(parent.BoundProperty, parentVal);
        var nextCover = await db.Set<TFile>()
          .Where(lambda)
          .OrderBy(f => f.SortOrder)
          .ThenByDescending(f => f.Id)
          .FirstOrDefaultAsync(cancellationToken);

        if (nextCover is IHasCoverFile cf)
        {
          cf.IsCover = true;
        }
      }

      await db.SaveChangesAsync(cancellationToken);
    }

    return new GridDeleteDto
    {
      Outcome = MutationOutcome.Committed,
      Deleted = list.Count
    };
  }

  [RequireAction(EndpointAction.Edit)]
  public virtual async Task<GridActionDto> SetCover(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentId = request.ParentId ?? ParentId;
    if (IsReadOnly || !HasCovers)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault();
    if (!targetId.HasValue)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var target = await RestrictedQuery().FirstOrDefaultAsync(f => f.Id == targetId.Value, cancellationToken);
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

  [RequireAction(EndpointAction.Edit)]
  public virtual async Task<GridActionDto> Move(FileMoveRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentId = request.ParentId ?? ParentId;
    if (IsReadOnly)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault();
    if (!targetId.HasValue)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var target = await RestrictedQuery().FirstOrDefaultAsync(f => f.Id == targetId.Value, cancellationToken);
    if (target == null)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    if (request.BeforeId == target.Id)
    {
      return new GridActionDto { Outcome = MutationOutcome.Committed };
    }

    var parent = GetParent();

    var siblingsQuery = RestrictedQuery().Where(f => f.Id != target.Id);
    if (parent.BoundProperty != null && parent.BoundProperty.GetValue(target) is { } parentVal)
    {
      siblingsQuery = siblingsQuery.Where(BuildParentFilter(parent.BoundProperty, parentVal));
    }

    var others = await siblingsQuery
      .OrderBy(f => f.SortOrder)
      .ThenByDescending(f => f.Id)
      .ToListAsync(cancellationToken);

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

    await db.SaveChangesAsync(cancellationToken);
    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  [RequireAction(EndpointAction.Edit)]
  public virtual async Task<GridActionDto> SetInfo(FileInfoRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ParentId = request.ParentId ?? ParentId;
    if (IsReadOnly)
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

    var file = await RestrictedQuery().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
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
    await db.SaveChangesAsync(cancellationToken);

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  async Task<long?> IFormFilesSource.AttachFileAsync(
    long parentId,
    string fileName,
    string contentType,
    byte[] content,
    CancellationToken cancellationToken
  )
  {
    if (IsReadOnly || parentId <= 0 || content.Length == 0)
    {
      return null;
    }

    var cleanedName = FileTypeTable.NameOf(fileName) ?? "attachment";
    var rules = FileRulesAttribute.Of(typeof(TFile));
    var normalizedType = FileTypeTable.Normalize(contentType) ?? "application/octet-stream";
    var sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

    var parent = GetParent();
    ParentId = parentId;

    List<TFile>? siblings = null;
    if (parent.BoundProperty != null)
    {
      siblings = await RestrictedQuery()
        .OrderBy(f => f.SortOrder)
        .ThenByDescending(f => f.Id)
        .ToListAsync(cancellationToken);

      if (siblings.FirstOrDefault(s => string.Equals(s.Sha256, sha256, StringComparison.OrdinalIgnoreCase)) is { } duplicate)
      {
        return duplicate.Id;
      }
    }

    var storageKey = FileKeys.New();
    var storeKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
    var storage = Stores.Of(storeKey);

    await using (var stream = new MemoryStream(content, writable: false))
    {
      await storage.WriteAsync(storageKey, stream, cancellationToken);
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

    await db.Set<TFile>().AddAsync(entity, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);

    await AfterUploadAsync(entity, content, cancellationToken);
    if (db.ChangeTracker.HasChanges())
    {
      await db.SaveChangesAsync(cancellationToken);
    }

    return entity.Id;
  }
}
