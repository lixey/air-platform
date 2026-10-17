namespace DemoEcom.Documents;

public abstract class EcomDocFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDoc>(db, ecom)
{
  protected abstract long Type { get; }


  protected override void SetHooks(Hooks hooks)
  {
    hooks.BeforeSave(async (c, ct) =>
    {
      if (c.IsNew)
      {
        c.Entity.DocTypeId = Type;
        if (c.Entity.Date == default)
        {
          c.Entity.Date = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        if (string.IsNullOrWhiteSpace(c.Entity.Number))
        {
          var count = await db.Set<EcomDoc>().CountAsync(d => d.DocTypeId == Type, ct) + 1;
          c.Entity.Number = $"{Type}-{count:D5}";
        }
      }
    });
  }
}
