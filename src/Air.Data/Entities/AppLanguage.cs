using Microsoft.EntityFrameworkCore;

namespace Air.Data.Entities;

[Comment("Supported platform language reference entity"), Index(nameof(Handle), IsUnique = true), Index(nameof(Name), IsUnique = true)]
public class AppLanguage : BaseDictionaryEntity, IReadOnlyEntity
{
}
