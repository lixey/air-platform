using System.Linq.Expressions;
using Air.App.Form;
using Air.App.Form.Actions;
using Air.App.Grid.Selection;
using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Documents;

public static class EcomDocActions
{
  public const string NotEditable = "doc.notEditable";
  public const string NoteFull = "doc.noteFull";
  public const int ReasonMaxLength = 200;
  public const string ReasonPath = "reason";

  private static long[] CounterKey(EcomDoc doc) => [doc.OrganizationId, doc.DocTypeId, doc.Date.Year];

  public static void AskReason<TInput>(ActionInput<TInput> input, Expression<Func<TInput, string?>> reason)
    where TInput : class
  {
    ArgumentNullException.ThrowIfNull(input);
    input.Add(reason).Label("Reason").AsTextarea().Required().Rules(r => r.MaximumLength(ReasonMaxLength));
  }

  public static async Task SaveAsync(
    EcomDbContext db,
    FormSaveContext<EcomDoc, EcomDoc> context,
    long? userId,
    EcomDocStatus status,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(status);
    var doc = context.Entity;
    if (context.IsNew)
    {
      await EcomDocNumbers.IssueAsync(db, doc, cancellationToken);
      await EcomDocDefaults.FromOrganizationAsync(db, doc, cancellationToken);
    }

    if (status.IsEditable)
    {
      await EcomDocDefaults.FromCustomerAsync(db, doc, userId, cancellationToken);
      await EcomDocTotals.ApplyAsync(db, doc, cancellationToken);
    }
  }

  public static async Task EnterAsync<TFormService>(
    EcomDbContext db,
    GridBatchContext<EcomDoc> context,
    long targetTypeId,
    long targetStatusId,
    DateOnly date,
    EcomDocTexts texts,
    CancellationToken cancellationToken
  ) where TFormService : class
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(texts);
    var sources = context.Records.OrderBy(doc => doc.Id).ToList();
    var statusIds = sources.Select(doc => doc.DocStatusId).Distinct().ToList();
    var posted = (await db.EcomDocStatus.AsNoTracking().Where(status => statusIds.Contains(status.Id) && status.IsPosted)
      .Select(status => status.Id).ToListAsync(cancellationToken)).ToHashSet();
    var sourceIds = sources.Where(doc => posted.Contains(doc.DocStatusId)).Select(doc => doc.Id).ToList();
    var lines = (await db.EcomDocLine.AsNoTracking().Where(line => sourceIds.Contains(line.DocId)).ToListAsync(cancellationToken))
      .ToLookup(line => line.DocId);
    var taken = await EcomDocEntering.TakenAsync(db, [.. lines.SelectMany(of => of).Select(line => line.Id)], null, cancellationToken);

    var entries = new List<EcomDocEntry>();
    var refused = false;
    foreach (var source in sources)
    {
      var entry = posted.Contains(source.DocStatusId)
        ? EcomDocEntering.Enter(
          source, [.. lines[source.Id]], line => line.Qty - taken.GetValueOrDefault(line.Id), targetTypeId, targetStatusId, date)
        : null;
      if (entry is null)
      {
        var code = posted.Contains(source.DocStatusId) ? EcomDocRefusal.NothingOpen : EcomDocRefusal.SourceNotPosted;
        context.AddError(code, texts.Entering(code), source.Id);
        refused = true;
        continue;
      }

      entries.Add(entry);
    }

    if (refused)
    {
      return;
    }

    await context.LockAsync<EcomDocCounter>(entries.Select(entry => CounterKey(entry.Doc)), cancellationToken);
    foreach (var entry in entries)
    {
      await EcomDocNumbers.IssueAsync(db, entry.Doc, cancellationToken);
      db.Add(entry.Doc);
      db.AddRange(entry.Lines);
      db.AddRange(entry.Links);
    }

    await OpenOneAsync<TFormService>(context, entries, cancellationToken);
  }

  public static async Task PayAsync<TFormService>(
    EcomDbContext db,
    GridBatchContext<EcomDoc> context,
    long targetTypeId,
    long targetStatusId,
    DateOnly date,
    EcomDocTexts texts,
    CancellationToken cancellationToken
  ) where TFormService : class
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(texts);
    var sources = context.Records.OrderBy(doc => doc.Id).ToList();
    var statusIds = sources.Select(doc => doc.DocStatusId).Distinct().ToList();
    var posted = (await db.EcomDocStatus.AsNoTracking().Where(status => statusIds.Contains(status.Id) && status.IsPosted)
      .Select(status => status.Id).ToListAsync(cancellationToken)).ToHashSet();
    var taken = await EcomDocPayments.TakenAsync(
      db, [.. sources.Where(doc => posted.Contains(doc.DocStatusId)).Select(doc => doc.Id)], cancellationToken);

    var open = new List<(EcomDoc Source, decimal Amount)>();
    var refused = false;
    foreach (var source in sources)
    {
      var amount = posted.Contains(source.DocStatusId) ? EcomDocPayments.Open(source, taken.GetValueOrDefault(source.Id)) : 0m;
      if (amount <= 0m)
      {
        var code = posted.Contains(source.DocStatusId) ? EcomDocRefusal.NothingOpen : EcomDocRefusal.SourceNotPosted;
        context.AddError(code, texts.Entering(code), source.Id);
        refused = true;
        continue;
      }

      open.Add((source, amount));
    }

    if (refused)
    {
      return;
    }

    var organizations = sources.Select(doc => doc.OrganizationId).Distinct().ToList();
    var accounts = await db.OrgMoneyAccount.AsNoTracking().Where(account => organizations.Contains(account.OrganizationId))
      .ToListAsync(cancellationToken);
    var methods = sources.Where(doc => doc.PaymentMethodId is not null).Select(doc => doc.PaymentMethodId!.Value).Distinct().ToList();
    var cash = (await db.OrgPaymentMethod.AsNoTracking().Where(method => methods.Contains(method.Id) && method.IsCash)
      .Select(method => method.Id).ToListAsync(cancellationToken)).ToHashSet();
    var entries = open.Select(payment =>
    {
      var (source, amount) = payment;
      var account = EcomMoney.Account(accounts, source.OrganizationId, source.PaymentMethodId is { } method && cash.Contains(method));
      return EcomDocPayments.Enter(source, targetTypeId, targetStatusId, date, amount, account);
    }).ToList();

    await context.LockAsync<EcomDocCounter>(entries.Select(entry => CounterKey(entry.Doc)), cancellationToken);
    foreach (var entry in entries)
    {
      await EcomDocNumbers.IssueAsync(db, entry.Doc, cancellationToken);
      db.Add(entry.Doc);
      db.AddRange(entry.Links);
    }

    await OpenOneAsync<TFormService>(context, entries, cancellationToken);
  }

  public static async Task PurchaseAsync(
    EcomDbContext db,
    GridBatchContext<EcomDocLine> context,
    long? userId,
    DateOnly date,
    EcomDocTexts texts,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(texts);
    var lines = context.Records.Where(line => EcomDocEntering.ToPurchase(line) > 0m).ToList();
    if (lines.Count == 0)
    {
      context.AddError(EcomDocRefusal.NothingToPurchase, texts.NothingToPurchase);
      return;
    }

    var orderIds = lines.Select(line => line.DocId).Distinct().ToList();
    var productIds = lines.Select(line => line.ProductId).Distinct().ToList();
    var orders = await db.EcomDoc.AsNoTracking().Where(doc => orderIds.Contains(doc.Id)).ToDictionaryAsync(doc => doc.Id, cancellationToken);
    var products = await db.OrgProduct.AsNoTracking().Where(product => productIds.Contains(product.Id))
      .ToDictionaryAsync(product => product.Id, cancellationToken);

    var wanted = lines.OrderBy(line => line.DocId).ThenBy(line => line.SortOrder).ThenBy(line => line.Id)
      .Select(line => new EcomPurchase(orders[line.DocId], line, products[line.ProductId], EcomDocEntering.ToPurchase(line))).ToList();
    var refused = false;
    foreach (var first in wanted.Where(purchase => purchase.Order.WarehouseId is null).DistinctBy(purchase => purchase.Order.Id))
    {
      context.AddError(EcomDocRefusal.NoWarehouse, texts.Purchasing(EcomDocRefusal.NoWarehouse), first.Line.Id);
      refused = true;
    }

    if (refused)
    {
      return;
    }

    var entries = EcomDocEntering.EnterPurchases(wanted, userId, date);
    await context.LockAsync<EcomDocCounter>(entries.Select(entry => CounterKey(entry.Doc)), cancellationToken);
    foreach (var entry in entries)
    {
      await EcomDocNumbers.IssueAsync(db, entry.Doc, cancellationToken);
      db.Add(entry.Doc);
      db.AddRange(entry.Lines);
      db.AddRange(entry.Links);
    }
  }

  public static async Task RefuseDeleteAsync(
    EcomDbContext db,
    GridDeleteContext<EcomDoc> context,
    long typeId,
    EcomDocTexts texts,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(texts);
    var editable = (await EcomDocStatuses.OfTypeAsync(db, typeId, cancellationToken))
      .Where(status => status.IsEditable).Select(status => status.Id).ToHashSet();
    foreach (var doc in context.Records.Where(doc => !editable.Contains(doc.DocStatusId)))
    {
      context.AddError(NotEditable, texts.NotEditable, doc.Id);
    }
  }

  private static async Task OpenOneAsync<TFormService>(
    GridBatchContext<EcomDoc> context,
    IReadOnlyList<EcomDocEntry> entries,
    CancellationToken cancellationToken
  ) where TFormService : class
  {
    if (entries.Count != 1)
    {
      return;
    }

    await context.FlushAsync(cancellationToken);
    context.Open<TFormService>(entries[0].Doc.Id);
  }
}
