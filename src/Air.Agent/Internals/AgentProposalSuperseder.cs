namespace Platform.Agent.Internals;

internal static class AgentProposalSuperseder
{
  public static IReadOnlyList<string> SupersedeOverlapping(
    AgentSessionState session,
    string newTurnId,
    IReadOnlyCollection<(string Service, long RecordId)> editedRecords,
    IReadOnlyCollection<(string Service, string Name)> createdNames)
  {
    var superseded = new List<string>();
    lock (session.Proposals)
    {
      var pendingOld = session.Proposals
        .Where(p => p.Status == "pending" && !string.Equals(p.TurnId, newTurnId, StringComparison.OrdinalIgnoreCase))
        .ToList();
      if (pendingOld.Count == 0)
      {
        return [];
      }

      var matchedTurnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var matchedDirectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      foreach (var old in pendingOld)
      {
        if (MatchesCandidate(old, editedRecords, createdNames))
        {
          if (!string.IsNullOrEmpty(old.TurnId))
          {
            matchedTurnIds.Add(old.TurnId);
          }
          else
          {
            matchedDirectIds.Add(old.Id);
          }
        }
      }

      foreach (var old in pendingOld)
      {
        if (!string.IsNullOrEmpty(old.TurnId) && matchedTurnIds.Contains(old.TurnId) ||
            matchedDirectIds.Contains(old.Id))
        {
          lock (old)
          {
            if (old.Status == "pending")
            {
              old.Status = "superseded";
              superseded.Add(old.Id);
              session.SupersededProposalIds.Enqueue(old.Id);
            }
          }
        }
      }
    }

    return superseded;
  }

  private static bool MatchesCandidate(
    AgentProposalDto proposal,
    IReadOnlyCollection<(string Service, long RecordId)> edits,
    IReadOnlyCollection<(string Service, string Name)> creates)
  {
    var normSvc = AgentFeatureRegistry.NormalizeServiceKey(proposal.Service);
    if (proposal.RecordId is > 0 && edits.Any(e =>
          e.RecordId == proposal.RecordId.Value &&
          (string.IsNullOrEmpty(e.Service) || string.Equals(e.Service, normSvc, StringComparison.OrdinalIgnoreCase))))
    {
      return true;
    }

    if (proposal.RecordId is null)
    {
      var shortLabel = AgentStepGraphHelper.GetProposalShortLabel(proposal);
      if (creates.Any(c =>
            (string.IsNullOrEmpty(c.Service) || string.Equals(c.Service, normSvc, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(shortLabel, c.Name, StringComparison.OrdinalIgnoreCase)))
      {
        return true;
      }
    }

    return false;
  }
}
