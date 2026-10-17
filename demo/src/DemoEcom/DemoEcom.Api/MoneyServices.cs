using Air.App.Grid;
using Air.App.Modules;
using Air.Data;
using DemoEcom.Data;
using DemoEcom.Data.Models;
using DemoOrg;
using DemoOrg.Customer;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Money;

public sealed class EcomIncomingPaymentGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.IncomingPayment;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>()
      .Include(d => d.Customer)
      .Where(d => d.DocTypeId == EcomData.DocType.IncomingPaymentId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Payment Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Customer).Label("Customer").AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Amount).Label("Amount Received").AsMoney().Sum();
  }
}

public sealed class EcomOutgoingPaymentGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.OutgoingPayment;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>().Where(d => d.DocTypeId == EcomData.DocType.OutgoingPaymentId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Payment Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Amount).Label("Amount Paid").AsMoney().Sum();
  }
}

public sealed class EcomMoneyBalanceGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.MoneyBalance;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>().Where(d => d.DocTypeId == EcomData.DocType.IncomingPaymentId || d.DocTypeId == EcomData.DocType.OutgoingPaymentId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Doc");
    columns.Add(r => r.Amount).Label("Balance").AsMoney().Sum();
  }
}

public sealed class EcomMoneyMoveGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.MoneyMove;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>().Where(d => d.DocTypeId == EcomData.DocType.IncomingPaymentId || d.DocTypeId == EcomData.DocType.OutgoingPaymentId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Doc");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}
