namespace DemoEcom.Documents;

public abstract class EcomDocBasisGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDoc>(db, ecom)
  where TDocForm : class
{
  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(d => d.Id);
}
