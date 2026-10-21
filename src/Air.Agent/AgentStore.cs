using System.Collections.Concurrent;

namespace Air.Agent;

public sealed class AgentStore
{
  private readonly ConcurrentDictionary<string, AgentAttachmentDto> _attachments = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, AgentProposalDto> _proposals = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, ConcurrentQueue<AgentProposalDto>> _sessionPendingProposals = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, string> _sessionConversations = new(StringComparer.OrdinalIgnoreCase);

  public string WorkspaceDirectory { get; }

  public AgentStore()
  {
    WorkspaceDirectory = Path.Combine(Path.GetTempPath(), "air-agent-workspace");
    Directory.CreateDirectory(WorkspaceDirectory);
  }

  public async Task<AgentAttachmentDto> SaveAttachmentAsync(
    string fileName,
    string contentType,
    Stream content,
    CancellationToken ct)
  {
    var id = Guid.NewGuid().ToString("N")[..12];
    var ext = Path.GetExtension(fileName);
    if (string.IsNullOrWhiteSpace(ext))
    {
      ext = contentType switch
      {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg"
      };
    }

    var safeFileName = $"receipt-{id}{ext}";
    var dir = Path.Combine(WorkspaceDirectory, "uploads");
    Directory.CreateDirectory(dir);
    var localPath = Path.Combine(dir, safeFileName);

    await using (var fs = File.Create(localPath))
    {
      await content.CopyToAsync(fs, ct);
    }

    var fileInfo = new FileInfo(localPath);
    var dto = new AgentAttachmentDto
    {
      Id = id,
      FileName = string.IsNullOrWhiteSpace(fileName) ? safeFileName : fileName,
      ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
      Size = fileInfo.Length,
      PreviewUrl = $"/api/agent/attachments/{id}",
      LocalPath = localPath
    };

    _attachments[id] = dto;
    return dto;
  }

  public AgentAttachmentDto? GetAttachment(string id) =>
    _attachments.TryGetValue(id, out var a) ? a : null;

  public void StageProposal(AgentProposalDto proposal)
  {
    _proposals[proposal.Id] = proposal;
    var queue = _sessionPendingProposals.GetOrAdd(proposal.SessionId, _ => new ConcurrentQueue<AgentProposalDto>());
    queue.Enqueue(proposal);
  }

  public AgentProposalDto? GetProposal(string proposalId) =>
    _proposals.TryGetValue(proposalId, out var p) ? p : null;

  public IReadOnlyList<AgentProposalDto> DrainPendingProposals(string sessionId)
  {
    if (!_sessionPendingProposals.TryGetValue(sessionId, out var queue))
    {
      return [];
    }

    var list = new List<AgentProposalDto>();
    while (queue.TryDequeue(out var p))
    {
      list.Add(p);
    }
    return list;
  }

  public string? GetConversationId(string sessionId) =>
    _sessionConversations.TryGetValue(sessionId, out var cid) ? cid : null;

  public void SetConversationId(string sessionId, string conversationId) =>
    _sessionConversations[sessionId] = conversationId;

  public void ResetSession(string sessionId)
  {
    _sessionConversations.TryRemove(sessionId, out _);
    _sessionPendingProposals.TryRemove(sessionId, out _);
  }
}
