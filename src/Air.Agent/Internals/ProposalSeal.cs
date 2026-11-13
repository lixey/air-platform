namespace Platform.Agent.Internals;

internal static class ProposalSeal
{
  private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

  public static string Compute(AgentProposalDto proposal, AgentOwnerScope owner) =>
    Convert.ToHexStringLower(ComputeBytes(proposal, owner));

  public static bool Verify(AgentProposalDto proposal, AgentOwnerScope owner, string? expectedSeal)
  {
    if (string.IsNullOrWhiteSpace(expectedSeal))
    {
      return false;
    }

    byte[] expectedBytes;
    try
    {
      expectedBytes = Convert.FromHexString(expectedSeal);
    }
    catch (FormatException)
    {
      return false;
    }

    var actualBytes = ComputeBytes(proposal, owner);
    return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
  }

  private static byte[] ComputeBytes(AgentProposalDto proposal, AgentOwnerScope owner)
  {
    var sb = new StringBuilder(512);
    AppendSegment(sb, owner.TenantId?.ToString(CultureInfo.InvariantCulture));
    AppendSegment(sb, owner.UserId?.ToString(CultureInfo.InvariantCulture));
    AppendSegment(sb, proposal.Id);
    AppendSegment(sb, proposal.SessionId);
    AppendSegment(sb, proposal.Operation);
    AppendSegment(sb, proposal.Service);
    AppendSegment(sb, proposal.RecordId?.ToString(CultureInfo.InvariantCulture));
    AppendSegment(sb, proposal.Transition);
    AppendSegment(sb, proposal.StepKey);
    AppendSegment(sb, string.Join('|', proposal.DependsOn ?? []));
    AppendSegment(sb, string.Join('|', proposal.AttachmentIds ?? []));
    var rawPayload = proposal.Payload.ValueKind == JsonValueKind.Undefined
      ? string.Empty
      : proposal.Payload.GetRawText();
    AppendSegment(sb, rawPayload);

    return HMACSHA256.HashData(ProcessKey, Encoding.UTF8.GetBytes(sb.ToString()));
  }

  private static void AppendSegment(StringBuilder sb, string? value)
  {
    var safe = value ?? string.Empty;
    sb.Append(safe.Length.ToString(CultureInfo.InvariantCulture));
    sb.Append(':');
    sb.Append(safe);
    sb.Append(';');
  }
}
