using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentTopologicalSorter
{
  public static List<JsonElement> SortInlineProposalsTopologically(IReadOnlyList<JsonElement> elements)
  {
    var n = elements.Count;
    if (n <= 1)
    {
      return [.. elements];
    }

    var keyToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var createNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < n; i++)
    {
      if (AgentStepGraphHelper.TryExtractStepKey(elements[i]) is { } sk && !keyToIndex.ContainsKey(sk))
      {
        keyToIndex[sk] = i;
      }

      var hasId = TryGetRecordId(elements[i]) is > 0 ||
                  elements[i].TryGetProperty("values", out var vp) && vp.ValueKind == JsonValueKind.Object && TryGetRecordId(vp) is > 0;
      if (!hasId && AgentStepGraphHelper.ExtractInlinePrimaryName(elements[i]) is { } cname && !createNameToIndex.ContainsKey(cname))
      {
        createNameToIndex[cname] = i;
      }
    }

    var adj = new List<int>[n];
    var indegree = new int[n];
    for (var i = 0; i < n; i++)
    {
      adj[i] = [];
    }

    void AddEdge(int from, int to)
    {
      if (from == to || adj[from].Contains(to))
      {
        return;
      }

      adj[from].Add(to);
      indegree[to]++;
    }

    for (var i = 0; i < n; i++)
    {
      foreach (var refKey in AgentStepGraphHelper.CollectReferencedKeys(elements[i]))
      {
        if (keyToIndex.TryGetValue(refKey, out var prereqIdx))
        {
          AddEdge(prereqIdx, i);
        }
      }

      if (createNameToIndex.Count > 0)
      {
        var vObj = elements[i].TryGetProperty("values", out var vp) && vp.ValueKind == JsonValueKind.Object ? vp : elements[i];
        if (vObj.ValueKind == JsonValueKind.Object)
        {
          foreach (var prop in vObj.EnumerateObject())
          {
            if (!string.Equals(prop.Name, "name", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(prop.Name, "title", StringComparison.OrdinalIgnoreCase) &&
                prop.Value.ValueKind == JsonValueKind.String &&
                prop.Value.GetString()?.Trim() is { Length: > 0 } strVal &&
                createNameToIndex.TryGetValue(strVal, out var nameIdx) &&
                nameIdx != i &&
                !adj[i].Contains(nameIdx))
            {
              AddEdge(nameIdx, i);
            }
          }
        }
      }
    }

    var ready = new SortedSet<int>();
    for (var i = 0; i < n; i++)
    {
      if (indegree[i] == 0)
      {
        ready.Add(i);
      }
    }

    var ordered = new List<JsonElement>(n);
    var visited = new bool[n];
    while (ready.Count > 0)
    {
      var cur = ready.Min;
      ready.Remove(cur);
      visited[cur] = true;
      ordered.Add(elements[cur]);

      foreach (var next in adj[cur])
      {
        if (--indegree[next] == 0)
        {
          ready.Add(next);
        }
      }
    }

    for (var i = 0; i < n; i++)
    {
      if (!visited[i])
      {
        ordered.Add(elements[i]);
      }
    }

    return ordered;
  }

  public static List<AgentProposalDto> SortStagedProposalsTopologically(
    IReadOnlyList<AgentProposalDto> proposals,
    IReadOnlyDictionary<string, StagedProposalPayload>? payloadsById = null)
  {
    var n = proposals.Count;
    if (n <= 1)
    {
      return [.. proposals];
    }

    var idToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < n; i++)
    {
      idToIndex[proposals[i].Id] = i;
    }

    var adj = new List<int>[n];
    var indegree = new int[n];
    for (var i = 0; i < n; i++)
    {
      adj[i] = [];
    }

    for (var i = 0; i < n; i++)
    {
      var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (proposals[i].DependsOn is { Count: > 0 } dList)
      {
        foreach (var d in dList)
        {
          deps.Add(d);
        }
      }

      var payload = payloadsById != null && payloadsById.TryGetValue(proposals[i].Id, out var memPayload)
        ? memPayload
        : proposals[i].Payload.ValueKind == JsonValueKind.Object
          ? proposals[i].Payload.Deserialize<StagedProposalPayload>(WebJson)
          : null;

      if (payload != null)
      {
        foreach (var d in payload.DependsOnProposalIds)
        {
          deps.Add(d);
        }

        foreach (var nr in payload.NewReferences)
        {
          if (!string.IsNullOrEmpty(nr.ProposalId))
          {
            deps.Add(nr.ProposalId);
          }
        }
      }

      foreach (var depId in deps)
      {
        if (idToIndex.TryGetValue(depId, out var depIdx) && depIdx != i && !adj[depIdx].Contains(i))
        {
          adj[depIdx].Add(i);
          indegree[i]++;
        }
      }
    }

    var ready = new SortedSet<int>();
    for (var i = 0; i < n; i++)
    {
      if (indegree[i] == 0)
      {
        ready.Add(i);
      }
    }

    var ordered = new List<AgentProposalDto>(n);
    while (ready.Count > 0)
    {
      var cur = ready.Min;
      ready.Remove(cur);
      ordered.Add(proposals[cur]);

      foreach (var next in adj[cur])
      {
        if (--indegree[next] == 0)
        {
          ready.Add(next);
        }
      }
    }

    if (ordered.Count < n)
    {
      throw AgentRefusal.Create(Localizer.Instance["MutationRefused"]);
    }

    return ordered;
  }
}
