namespace DemoEcom.Data.Models;

/// <summary>
/// A move of a document between two statuses of its type, a row of its history: the name by which the type's flow
/// declares the transition, the status left and the status taken, the user who took the move, none where the move had
/// no principal, the moment of the call and the note the move carried. The flow's event writes it in the transaction
/// of the move; a document's first status is no move and has no row. It goes with its document.
/// </summary>
[Index(nameof(TenantId), nameof(DocId), nameof(At))]
public sealed class EcomDocMove : AppBaseModel
{
  public long DocId { get; set; }
  public EcomDoc Doc { get; set; } = null!;

  [MaxLength(64)]
  public string Transition { get; set; } = string.Empty;

  public long FromId { get; set; }
  public EcomDocStatus From { get; set; } = null!;

  public long ToId { get; set; }
  public EcomDocStatus To { get; set; } = null!;

  public long? UserId { get; set; }
  public AppUser? User { get; set; }

  public DateTimeOffset At { get; set; }

  [MaxLength(2000)]
  public string? Note { get; set; }
}
