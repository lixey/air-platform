using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using Air.Data;
using DemoEcom.Data;
using DemoEcom.Data.Models;
using DemoOrg;
using DemoOrg.Data.Models;
using DemoOrg.Product;
using DemoOrg.Warehouse;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.WarehouseDoc;

public sealed class EcomStockReceiptGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.StockReceipt;
  protected override string FormServicePath => "ecom/stockreceiptform";

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>()
      .Include(d => d.Warehouse)
      .Include(d => d.Organization)
      .Where(d => d.DocTypeId == EcomData.DocType.StockReceiptId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<EcomStockReceiptFormService>();
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Warehouse).Label("Warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}

public sealed class EcomStockReceiptFormService(EcomDbContext db) : FormService<EcomDoc>(db)
{
  public override Feature Feature => Ecom.StockReceipt;
  protected override string Title => "Stock Receipt";

  protected override void SetElements(Elements<EcomDoc> elements)
  {
    elements.Add(e => e.Number).Label("Receipt Number").AsInfo().Span(4);
    elements.Add(e => e.Date).Label("Date").Span(4);
    elements.Add(e => e.WarehouseId).Label("Warehouse").AsReference<OrgWarehouseGridService>().Required().Span(4);
    elements.Add(e => e.Note).Label("Note").AsTextarea().Span(12);
  }
}

public sealed class EcomStockIssueGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.StockIssue;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>().Where(d => d.DocTypeId == EcomData.DocType.StockIssueId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Qty).Label("Quantity").Sum();
  }
}

public sealed class EcomStockTransferGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.StockTransfer;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>().Where(d => d.DocTypeId == EcomData.DocType.StockTransferId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Qty).Label("Quantity").Sum();
  }
}

public sealed class EcomStockBalanceRow
{
  public long Id { get; set; }
  public OrgWarehouse? Warehouse { get; set; }
  public OrgProduct? Product { get; set; }
  public string Article { get; set; } = string.Empty;
  public decimal Qty { get; set; }
  public decimal Reserved { get; set; }
  public decimal Expected { get; set; }
  public decimal Free { get; set; }
}

public sealed class EcomStockBalanceGridService(EcomDbContext db) : GridService<EcomStockBalance, EcomStockBalance>(db)
{
  public override Feature Feature => Ecom.StockBalance;

  protected override IQueryable<EcomStockBalance> Query(PlatformDbContext context) =>
    context.Set<EcomStockBalance>()
      .Include(b => b.Warehouse)
      .Include(b => b.Product);

  protected override void SetColumns(Columns<EcomStockBalance> columns)
  {
    columns.Add(r => r.Warehouse).Label("Warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Product).Label("Product").AsReference<OrgProductGridService>();
    columns.Add(r => r.Article).Label("Article / SKU");
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Reserved).Label("Reserved").Sum();
    columns.Add(r => r.Expected).Label("Expected").Sum();
  }
}

public sealed class EcomStockMoveGridService(EcomDbContext db) : GridService<EcomDocLine, EcomDocLine>(db)
{
  public override Feature Feature => Ecom.StockMove;

  protected override IQueryable<EcomDocLine> Query(PlatformDbContext context) =>
    context.Set<EcomDocLine>().Include(l => l.Product);

  protected override void SetColumns(Columns<EcomDocLine> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Product).Label("Product").AsReference<OrgProductGridService>();
    columns.Add(r => r.Article).Label("Article");
    columns.Add(r => r.Qty).Label("Quantity").Sum();
  }
}
