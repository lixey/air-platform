namespace DemoOrg.Data.Models;

[Index(nameof(Handle), IsUnique = true)]
public sealed class OrgPaymentTerm : BaseDictionaryEntity, IReadOnlyEntity;
