using System.Globalization;

namespace DemoEcom.Data.Documents;

/// <summary>
/// The numbers of documents: one sequence per organisation, document type and year of the document's date, kept by a
/// counter row.
/// </summary>
public static class EcomDocNumbers
{
  /// <summary>
  /// The number as it is written: the type's prefix, the organisation's code, the two last digits of the year and the
  /// number in six digits, more when it outgrows them (<c>CO-MAIN-26-000001</c>).
  /// </summary>
  public static string Format(string prefix, string organizationCode, int year, int number) =>
    string.Create(CultureInfo.InvariantCulture, $"{prefix}-{organizationCode}-{year % 100:00}-{number:000000}");

  /// <summary>
  /// Issues the next number to <paramref name="doc"/>: reads the tracked counter of its organisation, its type and the
  /// year of its date, adds one of the document's tenant when there is none, raises its last number and writes the
  /// document's number from the type's prefix and the organisation's code. It stages only: the caller saves the counter
  /// with the document. The screens take the mutation's lock of the counter's key before this call
  /// (<c>EcomDocActions</c>), so a second issue at once waits and reads the number the first left; an issue outside that
  /// lock fails its save by the counter's version, or by the unique index of the counters when both added the first one.
  /// </summary>
  public static async Task IssueAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    var year = doc.Date.Year;
    // A counter staged in this context and not saved yet is found among the tracked ones, which a query does not return
    var counter = db.EcomDocCounter.Local.FirstOrDefault(
                    c => c.OrganizationId == doc.OrganizationId && c.DocTypeId == doc.DocTypeId && c.Year == year
                  )
                  ?? await db.EcomDocCounter.FirstOrDefaultAsync(
                    c => c.OrganizationId == doc.OrganizationId && c.DocTypeId == doc.DocTypeId && c.Year == year,
                    cancellationToken
                  );
    if (counter is null)
    {
      counter = new EcomDocCounter { TenantId = doc.TenantId, OrganizationId = doc.OrganizationId, DocTypeId = doc.DocTypeId, Year = year };
      db.EcomDocCounter.Add(counter);
    }

    counter.LastNumber++;
    var prefix = await db.EcomDocType.Where(type => type.Id == doc.DocTypeId).Select(type => type.Prefix).SingleAsync(cancellationToken);
    var code = await db.OrgOrganization.Where(organization => organization.Id == doc.OrganizationId).Select(organization => organization.Code)
      .SingleAsync(cancellationToken);
    doc.Number = Format(prefix, code, year, counter.LastNumber);
  }
}
