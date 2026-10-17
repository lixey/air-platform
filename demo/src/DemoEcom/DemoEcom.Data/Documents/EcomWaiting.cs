namespace DemoEcom.Data.Documents;

/// <summary>
/// A customer order that waits for an arrival: the order in Confirmed or Paused, every line of it as the context tracks
/// them, and its status, as the posting of its reserve reads them.
/// </summary>
internal sealed record EcomWaiting(EcomDoc Order, List<EcomDocLine> Lines, EcomDocStatus Status);
