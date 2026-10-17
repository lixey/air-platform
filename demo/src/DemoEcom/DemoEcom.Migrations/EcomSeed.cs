using DemoApp.Migrations.Seeds;
using DemoEcom.Data;
using DemoEcom.Migrations.Seeds;
using Air.Data;

namespace DemoEcom.Migrations;

/// <summary>
/// Ecom's initial data: the rows of code, the document types and their statuses, synced with a check on every run, then,
/// written once under one installation mark, the examples, the same volume for each of the tenants it is given: the
/// customer orders, the stock documents, the sale of the orders, sales invoices and sales returns, the purchasing of
/// what the orders lack, purchase orders, supplier invoices and supplier returns, and the money, the capital each
/// organisation opens with and the incoming and outgoing payments of those documents, walked together in date order
/// (<see cref="EcomSeedWalk"/>), with their lines, their links, their moves and the balances those sum to, of the stock
/// and of the money. It runs after
/// Org's seed, whose organisations, warehouses, customers, suppliers and products the documents name. The installation
/// gives it the moment it runs, <c>now</c>: a tenant's documents are dated over the thirty days before that moment's
/// day by the tenant's own clock, the newest the day before. The seed reads no clock itself, so the same volume, random
/// seed, tenants and day write the same rows.
/// </summary>
public static class EcomSeed
{
  public static Task RunAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int seed,
    IReadOnlyList<AppSeedTenant> tenants,
    DateTimeOffset now,
    CancellationToken cancellationToken = default
  ) => RunAsync(db, volume, seed, tenants, now, purchasing: true, cancellationToken);

  /// <summary>
  /// The seed with its purchasing chains or, with <paramref name="purchasing"/> off, without them: the customer orders,
  /// the stock documents and the sales as the whole seed writes them, nothing purchased for a short order. That is what
  /// the purchasing is measured against: it moves no row of the sales and no free stock.
  /// </summary>
  public static Task RunAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int seed,
    IReadOnlyList<AppSeedTenant> tenants,
    DateTimeOffset now,
    bool purchasing,
    CancellationToken cancellationToken = default
  ) => RunAsync(db, volume, seed, tenants, now, purchasing, payments: true, cancellationToken);

  /// <summary>
  /// The seed with its payments or, with <paramref name="payments"/> off, without them: no capital, no payment, no money
  /// move and no money balance, and every other document as the whole seed writes it but for what was paid on it. That
  /// is what the money is measured against: it moves no row of the orders, the stock documents, the sales and the
  /// purchasing.
  /// </summary>
  public static async Task RunAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int seed,
    IReadOnlyList<AppSeedTenant> tenants,
    DateTimeOffset now,
    bool purchasing,
    bool payments,
    CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(volume);
    ArgumentNullException.ThrowIfNull(tenants);
    await EcomCodeRows.SyncAsync(db, cancellationToken);
    await Installation.RunOnceAsync(
      db,
      EcomData.ModuleHandle,
      token => EcomSeedWalk.RunAsync(db, volume, seed, tenants, now, purchasing, payments, token),
      cancellationToken
    );
  }
}
