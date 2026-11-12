using Platform.App.Files.Internals;

namespace Platform.Agent.Internals;

internal static class AgentDuplicateDetector
{
  public static async Task<(string? Warning, long? RecordId, string? FormService)> DetectFileDuplicateAsync(
    IFormService formInst,
    GridCreateDescriptor targetDesc,
    IReadOnlyList<GridCreateDescriptor> descriptors,
    Type? fileChildServiceType,
    IReadOnlyList<string> attachmentIds,
    AgentStore store,
    AgentOwnerScope owner,
    IServiceProvider rootServices,
    PlatformDbContext db,
    PlatformAppService app,
    Localizer L,
    CancellationToken ct)
  {
    if (attachmentIds.Count == 0 || fileChildServiceType == null)
    {
      return (null, null, null);
    }

    try
    {
      await using var fileScope = AgentModuleScope.OpenForService(rootServices, fileChildServiceType, db, app);
      if (ModuleCatalog.Instance.CreateServiceInstance(fileChildServiceType, fileScope.Db, fileScope.App) is not IFormFilesSource fileSource)
      {
        return (null, null, null);
      }

      long? duplicateId = null;
      foreach (var attId in attachmentIds)
      {
        var att = store.GetAttachment(attId, owner);
        if (att == null)
        {
          continue;
        }

        var sha256 = att.Sha256;
        if (string.IsNullOrWhiteSpace(sha256) && !string.IsNullOrEmpty(att.LocalPath) && File.Exists(att.LocalPath))
        {
          await using var stream = File.OpenRead(att.LocalPath);
          var hashBytes = await SHA256.HashDataAsync(stream, ct);
          sha256 = Convert.ToHexStringLower(hashBytes);
          att.Sha256 = sha256;
        }

        if (!string.IsNullOrWhiteSpace(sha256))
        {
          var existingParentId = await fileSource.FindExistingParentIdByHashAsync(sha256, ct);
          if (existingParentId is > 0)
          {
            duplicateId = existingParentId.Value;
            break;
          }
        }
      }

      if (duplicateId is not > 0)
      {
        return (null, null, null);
      }

      if (formInst.Feature.ServiceType != null &&
          typeof(IGridService).IsAssignableFrom(formInst.Feature.ServiceType))
      {
        await using var gridScope = AgentModuleScope.OpenForService(rootServices, formInst.Feature.ServiceType, db, app);
        if (ModuleCatalog.Instance.CreateServiceInstance(formInst.Feature.ServiceType, gridScope.Db, gridScope.App) is IGridService ownerGrid &&
            AgentFeatureRegistry.CanViewFeature(ownerGrid.Feature, gridScope.App))
        {
          var readRow = await ownerGrid.ReadRowAsync(duplicateId.Value, ct);
          if (readRow == null)
          {
            return (null, null, null);
          }
        }
      }

      var dupDesc = await AgentFeatureRegistry.ResolveEditFormDescriptorAsync(
        descriptors,
        targetDesc,
        targetDesc.ServicePath,
        duplicateId.Value,
        rootServices,
        db,
        app,
        ct);

      var summary = $"#{duplicateId.Value}";
      return (L["AgentDuplicateFileWarning", summary], duplicateId.Value, dupDesc.ServicePath);
    }
    catch
    {
      return (null, null, null);
    }
  }

  public static AgentProposalDto? FindExistingDuplicateProposal(
    AgentStore store,
    string sessionId,
    AgentOwnerScope owner,
    string? turnId,
    string? stepKey,
    long? recordId,
    string operation,
    string servicePath,
    string title,
    string? subtitle,
    List<string> attachmentIds,
    List<AgentProposalChildGroupDto> childGroups,
    List<AgentProposalFieldDto> displayFields,
    JsonSerializerOptions webJson) =>
    store.GetSessionProposals(sessionId, owner).FirstOrDefault(p =>
      p.Status == "pending" &&
      (turnId == null || string.Equals(p.TurnId, turnId, StringComparison.OrdinalIgnoreCase)) &&
      string.Equals(p.StepKey, stepKey, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(p.Operation, operation, StringComparison.OrdinalIgnoreCase) &&
      p.RecordId == recordId &&
      string.Equals(p.Service, servicePath, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(p.Subtitle, subtitle, StringComparison.OrdinalIgnoreCase) &&
      p.AttachmentIds.SequenceEqual(attachmentIds, StringComparer.Ordinal) &&
      p.Children.Count == childGroups.Count &&
      JsonSerializer.Serialize(p.Children, webJson) == JsonSerializer.Serialize(childGroups, webJson) &&
      p.Fields.Count == displayFields.Count &&
      p.Fields.Zip(displayFields).All(pair =>
        string.Equals(pair.First.Label, pair.Second.Label, StringComparison.Ordinal) &&
        string.Equals(pair.First.Value, pair.Second.Value, StringComparison.Ordinal)));

  public static AgentProposalDto? FindReferencedProposal(
    AgentStore store,
    string sessionId,
    AgentOwnerScope owner,
    string? turnId,
    string refKey,
    string? referenceService)
  {
    var sessionProposals = store.GetSessionProposals(sessionId, owner)
      .Where(p => p.Status is "pending" or "committed")
      .ToList();

    bool IsCompatible(AgentProposalDto p) =>
      string.IsNullOrWhiteSpace(referenceService) ||
      AgentStepGraphHelper.AreServicesEntityCompatible(referenceService, p.Service);

    if (!string.IsNullOrWhiteSpace(turnId))
    {
      return sessionProposals.LastOrDefault(p =>
               string.Equals(p.TurnId, turnId, StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(p.StepKey, refKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Id, refKey, StringComparison.OrdinalIgnoreCase)) &&
               IsCompatible(p))
             ?? sessionProposals.LastOrDefault(p => string.Equals(p.Id, refKey, StringComparison.OrdinalIgnoreCase) && IsCompatible(p));
    }

    return sessionProposals.LastOrDefault(p =>
      (p.TurnId == null && string.Equals(p.StepKey, refKey, StringComparison.OrdinalIgnoreCase) ||
       string.Equals(p.Id, refKey, StringComparison.OrdinalIgnoreCase)) &&
      IsCompatible(p));
  }
}
