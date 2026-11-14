using Platform.Api.Files.Internals;
using Platform.App.Files.Internals;

namespace Platform.App.Files;

public abstract partial class FileGridService<TFile>
  where TFile : BaseAppFile, new()
{
  [Endpoint]
  [RequireAction(Right.Edit)]
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
    if (!await EnsureParentScopeAsync(lockParent: false, cancellationToken) || IsReadOnly)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var file = files["file"] ?? files.FirstOrDefault();
    if (file == null || file.Length == 0)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [new MessageDto { Detail = L["FileCountRefused"], Code = "file.count", Type = MessageType.Error }]
      };
    }

    var rules = FileRulesAttribute.Of(typeof(TFile));
    if (FileUploadValidator.ValidateHeader(file.FileName, file.ContentType, file.Length, rules, L, out var cleanedName, out var normalizedType) is { } headerError)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [headerError]
      };
    }

    byte[] fileBytes;
    await using (var readStream = file.OpenReadStream())
    {
      using var ms = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
      await readStream.CopyToAsync(ms, cancellationToken);
      fileBytes = ms.ToArray();
    }

    if (FileUploadValidator.ValidateMagicBytes(normalizedType, fileBytes, L) is { } contentError)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [contentError]
      };
    }

    if (!FileUploadValidator.TryNormalizeMetadata(request.Metadata, L, out var normalizedMetadata, out var metadataError))
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [metadataError!]
      };
    }

    var sha256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
    ParentId = request.ParentId ?? ParentId;
    var thumbFile = files["thumbnail"];
    var servicePath = ServicePathResolver.Resolve(GetType());
    var rawPayload = request.RawPayload ?? $"{ParentService}|{ParentId}|{request.AfterId}|{sha256}|{normalizedMetadata}";

    return await MutationRunner.RunIdempotentMutationAsync<GridActionDto>(
      db,
      $"{servicePath}/upload",
      request.IdempotencyKey,
      rawPayload,
      mutationCt => SaveUploadedFileInMutationAsync(
        cleanedName,
        normalizedType,
        fileBytes,
        sha256,
        normalizedMetadata,
        thumbFile,
        request.AfterId,
        idempotentOnDuplicate: false,
        rules,
        mutationCt),
      cancellationToken,
      PlatformApp?.Services);
  }

  private async Task<GridActionDto> SaveUploadedFileInMutationAsync(
    string cleanedName,
    string normalizedType,
    byte[] fileBytes,
    string sha256,
    string? normalizedMetadata,
    IFormFile? thumbFile,
    long? afterId,
    bool idempotentOnDuplicate,
    FileRulesAttribute rules,
    CancellationToken mutationCt)
  {
    if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var parent = GetParent();
    var parentId = ParentId;
    List<TFile>? siblings = null;
    if (parent.BoundProperty == null || parentId.HasValue)
    {
      siblings = await RestrictedQuery()
        .AsTracking()
        .OrderBy(f => f.SortOrder)
        .ThenByDescending(f => f.Id)
        .ToListAsync(mutationCt);

      var duplicate = siblings.FirstOrDefault(s => string.Equals(s.Sha256, sha256, StringComparison.OrdinalIgnoreCase));
      if (idempotentOnDuplicate && duplicate != null)
      {
        return new GridActionDto { Outcome = MutationOutcome.Committed, Id = duplicate.Id };
      }

      if (rules.MaxFiles > 0 && siblings.Count >= rules.MaxFiles)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = [new MessageDto { Detail = L["FileCountLimit", rules.MaxFiles], Code = "file.limit", Type = MessageType.Error }]
        };
      }

      if (rules.MaxTotalBytes > 0 && siblings.Sum(s => s.Size) + fileBytes.Length > rules.MaxTotalBytes)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = [new MessageDto { Detail = L["FileTotalTooLarge", Math.Round(rules.MaxTotalBytes / (1024d * 1024d), 1)], Code = "file.limit", Type = MessageType.Error }]
        };
      }

      if (duplicate != null)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = [new MessageDto { Detail = L["FileDuplicate", duplicate.Name], Code = "file.duplicate", Type = MessageType.Error }]
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
    try
    {
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
        Size = fileBytes.Length,
        StorageKey = storageKey,
        ThumbnailKey = thumbKey,
        Store = storeKey,
        Sha256 = sha256,
        Metadata = normalizedMetadata,
        UploadedById = User?.UserId,
        CreatedAt = Time.UtcNow,
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

      if (siblings != null)
      {
        var place = afterId is { } aid && siblings.FindIndex(f => f.Id == aid) is >= 0 and var idx
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
    }
    catch
    {
      try
      {
        await storage.DeleteAsync(storageKey, CancellationToken.None);
      }
      catch { }

      if (thumbKey != null)
      {
        try
        {
          await storage.DeleteAsync(thumbKey, CancellationToken.None);
        }
        catch { }
      }

      throw;
    }
  }

  protected virtual Task AfterUploadAsync(TFile entity, byte[] content, CancellationToken cancellationToken) =>
    Task.CompletedTask;

  [RequireAction(Right.Edit)]
  public override Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    DeleteAsync(request, cancellationToken);

  public override async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var rules = FileRulesAttribute.Of(typeof(TFile));
    var keysToDelete = new List<(string? Store, string Key)>();

    var result = await RunFileMutationAsync(
      request,
      "delete",
      $"{request.ParentService ?? ParentService}|{request.ParentId ?? ParentId}|{request.Id}|{string.Join(',', request.SelectedIds ?? [])}",
      CanDelete,
      static () => new GridDeleteDto { Outcome = MutationOutcome.Refused, Deleted = 0 },
      async (ids, mutationCt) =>
      {
        keysToDelete.Clear();
        var list = await RestrictedQuery().AsTracking().Where(f => ids.Contains(f.Id)).ToListAsync(mutationCt);
        if (list.Count == 0)
        {
          return new GridDeleteDto { Outcome = MutationOutcome.Committed, Deleted = 0 };
        }

        var parent = GetParent();
        var parentsLosingCover = new HashSet<object>();
        keysToDelete.AddRange(FileStorageCleaner.CollectKeys(list));

        foreach (var item in list)
        {
          if (HasCovers && rules.Cover == FileCover.Auto && parent.BoundProperty != null &&
              item is IHasCoverFile { IsCover: true } &&
              parent.BoundProperty.GetValue(item) is { } parentVal)
          {
            parentsLosingCover.Add(parentVal);
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
      },
      cancellationToken);

    if (result.Outcome == MutationOutcome.Committed && keysToDelete.Count > 0)
    {
      await FileStorageCleaner.DeleteUnreferencedBlobsAsync<TFile>(db, Stores, keysToDelete, CancellationToken.None);
    }

    return result;
  }

  [Endpoint]
  [RequireAction(Right.Edit)]
  public virtual Task<GridActionDto> SetCover(GridRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    return RunFileMutationAsync(
      request,
      "set-cover",
      $"{request.ParentService ?? ParentService}|{request.ParentId ?? ParentId}|{request.Id}|{string.Join(',', request.SelectedIds ?? [])}",
      () => !IsReadOnly && HasCovers,
      static () => new GridActionDto { Outcome = MutationOutcome.Refused },
      async (ids, mutationCt) =>
      {
        if (ids.Count == 0)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused };
        }

        var targetId = ids[0];
        var target = await RestrictedQuery().AsTracking().FirstOrDefaultAsync(f => f.Id == targetId, mutationCt);
        if (target == null)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused };
        }

        var parent = GetParent();
        if (parent.BoundProperty != null)
        {
          if (parent.BoundProperty.GetValue(target) is { } parentVal)
          {
            var lambda = BuildParentFilter(parent.BoundProperty, parentVal);
            var siblings = await db.Set<TFile>().AsTracking().Where(lambda).ToListAsync(mutationCt);
            foreach (var sibling in siblings)
            {
              if (sibling is IHasCoverFile cf)
              {
                cf.IsCover = sibling.Id == targetId;
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
      },
      cancellationToken,
      preferSingleId: true);
  }

  [Endpoint]
  [RequireAction(Right.Edit)]
  public virtual Task<GridActionDto> Move(FileMoveRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    return RunFileMutationAsync(
      request,
      "move",
      $"{request.ParentService ?? ParentService}|{request.ParentId ?? ParentId}|{request.Id}|{request.BeforeId}|{string.Join(',', request.SelectedIds ?? [])}",
      () => !IsReadOnly,
      static () => new GridActionDto { Outcome = MutationOutcome.Refused },
      async (ids, mutationCt) =>
      {
        if (ids.Count == 0)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused };
        }

        var targetId = ids[0];
        var target = await RestrictedQuery().AsTracking().FirstOrDefaultAsync(f => f.Id == targetId, mutationCt);
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
      },
      cancellationToken,
      preferSingleId: true);
  }

  [Endpoint]
  [RequireAction(Right.Edit)]
  public virtual Task<GridActionDto> SetInfo(FileInfoRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    return RunFileMutationAsync(
      request,
      "set-info",
      $"{request.ParentService ?? ParentService}|{request.ParentId ?? ParentId}|{request.Id}|{request.Name}|{request.Description}",
      () => !IsReadOnly,
      static () => new GridActionDto { Outcome = MutationOutcome.Refused },
      async (ids, mutationCt) =>
      {
        var stem = request.Name?.Trim() ?? string.Empty;
        if (stem.Length == 0 || stem.Any(c => c is '/' or '\\' || char.IsControl(c)))
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["FileNameInvalid"], Path = "name", Code = "file.name", Type = MessageType.Error }]
          };
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description?.Length > BaseAppFile.DescriptionMaxLength)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["FileDescriptionTooLong"], Path = "description", Code = "form.validation.maxLength", Type = MessageType.Error }]
          };
        }

        var id = ids.Count > 0 ? ids[0] : throw new InvalidOperationException(L["FileIdRequired"]);
        var file = await RestrictedQuery().AsTracking().FirstOrDefaultAsync(f => f.Id == id, mutationCt)
          ?? throw new KeyNotFoundException(L["FileNotFound"]);

        var ext = Path.GetExtension(file.Name);
        var candidateName = stem.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? stem : stem + ext;
        if (FileTypeTable.NameOf(candidateName) is not { } newName ||
            !string.Equals(Path.GetExtension(newName), ext, StringComparison.OrdinalIgnoreCase) ||
            newName.Length > BaseAppFile.NameMaxLength)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["FileNameRefused"], Path = "name", Code = "file.name", Type = MessageType.Error }]
          };
        }

        file.Name = newName;
        file.Description = description;
        await db.SaveChangesAsync(mutationCt);

        return new GridActionDto { Outcome = MutationOutcome.Committed };
      },
      cancellationToken,
      preferSingleId: true);
  }

  private async Task<TResult> RunFileMutationAsync<TResult>(
    GridRequest request,
    string actionName,
    string fallbackPayload,
    Func<bool> canExecute,
    Func<TResult> refusedResult,
    Func<IReadOnlyList<long>, CancellationToken, Task<TResult>> execute,
    CancellationToken ct,
    bool preferSingleId = false)
    where TResult : MutationResultDto, new()
  {
    ParentService = request.ParentService ?? ParentService;
    ParentId = request.ParentId ?? ParentId;
    var servicePath = ServicePathResolver.Resolve(GetType());
    var rawPayload = request.RawPayload ?? fallbackPayload;

    return await MutationRunner.RunIdempotentMutationAsync(
      db,
      $"{servicePath}/{actionName}",
      request.IdempotencyKey,
      rawPayload,
      async mutationCt =>
      {
        var ids = ResolveSelectedIds(request, preferSingleId);
        await InferBoundParentIdFromIdsAsync(ids, mutationCt);
        if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || !canExecute())
        {
          return refusedResult();
        }

        return await execute(ids, mutationCt);
      },
      ct,
      PlatformApp?.Services);
  }

  private static IReadOnlyList<long> ResolveSelectedIds(GridRequest request, bool preferSingleId = false)
  {
    if (preferSingleId && request.Id.HasValue)
    {
      return [request.Id.Value];
    }

    if (request.SelectedIds is { Count: > 0 } selected)
    {
      return selected;
    }

    return request.Id.HasValue ? [request.Id.Value] : [];
  }

  async Task<GridActionDto> IFormFilesSource.AttachFileAsync(
    long parentId,
    string fileName,
    string contentType,
    byte[] content,
    string? parentService,
    CancellationToken cancellationToken
  )
  {
    if (parentId <= 0 || content.Length == 0)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [new MessageDto { Detail = L[parentId <= 0 ? "RequestInvalid" : "FileRefused"], Code = "file.invalid", Type = MessageType.Error }]
      };
    }

    var parent = GetParent();
    ParentId = parentId;
    if (!string.IsNullOrWhiteSpace(parentService))
    {
      ParentService = parentService;
    }
    else if (parent.BoundParentServiceType is { IsAbstract: false })
    {
      ParentService ??= parent.BoundParentService;
    }

    var rules = FileRulesAttribute.Of(typeof(TFile));
    if (FileUploadValidator.ValidateHeader(fileName, contentType, content.Length, rules, L, out var cleanedName, out var normalizedType) is { } headerError)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [headerError]
      };
    }

    if (FileUploadValidator.ValidateMagicBytes(normalizedType, content, L) is { } contentError)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [contentError]
      };
    }

    var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));

    return await db.RunMutationAsync(
      mutationCt => SaveUploadedFileInMutationAsync(
        cleanedName,
        normalizedType,
        content,
        sha256,
        normalizedMetadata: null,
        thumbFile: null,
        afterId: null,
        idempotentOnDuplicate: true,
        rules,
        mutationCt),
      ct: cancellationToken);
  }
}
