namespace DemoEcom.Data.Models;

/// <summary>
/// The last number issued to the documents of one type in one organisation and one year: a single row per the three, whose
/// version refuses a second write made outside the lock the screens take.
/// </summary>
[Index(nameof(TenantId), nameof(OrganizationId), nameof(DocTypeId), nameof(Year), IsUnique = true)]
public sealed class EcomDocCounter : AppBaseModel
{
  public long OrganizationId { get; set; }
  public OrgOrganization Organization { get; set; } = null!;

  public long DocTypeId { get; set; }
  public EcomDocType DocType { get; set; } = null!;

  public int Year { get; set; }

  public int LastNumber { get; set; }
}
