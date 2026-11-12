namespace DemoEcom.SalesReport;

public sealed class EcomSalesReportService(EcomDbContext db, EcomService? ecom = null) : ReportService(ecom)
{
  public override Feature Feature => Ecom.SalesReport;

  public override async Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default)
  {
    var rawPeriod = request.Filters != null && request.Filters.TryGetValue("period", out var p) && !string.IsNullOrWhiteSpace(p)
      ? p
      : "year";
    var period = rawPeriod is "30d" or "90d" or "year" or "12m" ? rawPeriod : "year";

    var today = Time.Today;
    var fromDate = period switch
    {
      "30d" => today.AddDays(-30),
      "90d" => today.AddDays(-90),
      "12m" => today.AddMonths(-12),
      _ => new DateOnly(today.Year, 1, 1)
    };

    var currencies = await db.OrgCurrency
      .AsNoTracking()
      .OrderBy(c => c.Id)
      .Select(c => c.Code)
      .ToListAsync(ct);
    if (currencies.Count == 0)
    {
      currencies = [OrgCurrencyData.UahCode];
    }

    var defaultCurrency = currencies.Contains(OrgCurrencyData.UahCode)
      ? OrgCurrencyData.UahCode
      : currencies[0];
    var currency = request.Filters != null &&
                   request.Filters.TryGetValue("currency", out var reqCur) &&
                   currencies.Any(c => string.Equals(c, reqCur, StringComparison.OrdinalIgnoreCase))
      ? currencies.First(c => string.Equals(c, reqCur, StringComparison.OrdinalIgnoreCase))
      : defaultCurrency;

    var baseQuery = db.EcomDoc
      .AsNoTracking()
      .Where(d => d.DocTypeId == EcomDocTypeData.CustomerOrderId &&
                  d.Date >= fromDate &&
                  d.Currency.Code == currency);

    var summaryRows = await baseQuery
      .Select(d => new
      {
        d.Date,
        d.DocStatusId,
        StatusName = d.DocStatus.Name,
        StatusColor = d.DocStatus.Color.Handle,
        d.Amount,
        d.PaidAmount
      })
      .ToListAsync(ct);

    var recentOrders = await baseQuery
      .OrderByDescending(d => d.Date)
      .ThenByDescending(d => d.Id)
      .Take(25)
      .Select(d => new
      {
        d.Id,
        d.Number,
        d.Date,
        StatusName = d.DocStatus.Name,
        StatusColor = d.DocStatus.Color.Handle,
        CustomerName = d.Customer != null ? d.Customer.Name : string.Empty,
        d.Amount,
        d.PaidAmount
      })
      .ToListAsync(ct);

    var activeOrders = summaryRows.Where(o => o.DocStatusId != EcomDocStatusData.CustomerOrderCanceledId).ToList();
    var totalOrders = activeOrders.Count;
    var totalRevenue = activeOrders.Sum(o => o.Amount);
    var totalPaid = activeOrders.Sum(o => o.PaidAmount);
    var collectionRate = totalRevenue > 0m ? Math.Round((totalPaid / totalRevenue) * 100m, 1) : 0m;

    var monthly = activeOrders
      .GroupBy(o => (o.Date.Year, o.Date.Month))
      .OrderBy(g => g.Key.Year)
      .ThenBy(g => g.Key.Month)
      .Select(g => (
        Label: new DateOnly(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy", CultureInfo.CurrentUICulture),
        Revenue: g.Sum(x => x.Amount),
        Paid: g.Sum(x => x.PaidAmount)
      ))
      .ToList();

    var byStatus = summaryRows
      .GroupBy(o => (o.DocStatusId, o.StatusName, o.StatusColor))
      .OrderBy(g => g.Key.DocStatusId)
      .Select(g => new ReportPieSliceDto
      {
        Name = L[g.Key.StatusName],
        Value = g.Count(),
        Color = g.Key.StatusColor
      })
      .ToList();

    var canOpenOrder = ecom?.Can(Ecom.CustomerOrder.View) == true;
    var orderFormService = ServicePathResolver.Resolve<EcomCustomerOrderFormService>();
    var topRows = recentOrders
      .Select(o => new RowDto
      {
        Id = o.Id,
        Open = canOpenOrder ? new OpenTargetDto { Service = orderFormService, Id = o.Id } : null,
        Cells = new Dictionary<string, object?>
        {
          ["number"] = o.Number,
          ["date"] = o.Date,
          ["customer"] = o.CustomerName,
          ["status"] = L[o.StatusName],
          ["amount"] = o.Amount,
          ["paid"] = o.PaidAmount
        },
        Colors = new Dictionary<string, string>
        {
          ["status"] = o.StatusColor
        }
      })
      .ToList();

    var filterGroups = new List<ReportFilterGroupDto>
    {
      new()
      {
        Key = "period",
        Label = L["Period"],
        Items =
        [
          new ReportFilterItemDto { Key = "30d", Label = L["30 days"] },
          new ReportFilterItemDto { Key = "90d", Label = L["90 days"] },
          new ReportFilterItemDto { Key = "year", Label = L["This year"] },
          new ReportFilterItemDto { Key = "12m", Label = L["12 months"] }
        ]
      }
    };
    if (currencies.Count > 1)
    {
      filterGroups.Add(new ReportFilterGroupDto
      {
        Key = "currency",
        Label = L["Currency"],
        Items = [.. currencies.Select(c => new ReportFilterItemDto { Key = c, Label = c })]
      });
    }

    return new ReportDataDto
    {
      ActiveFilters = new Dictionary<string, string>
      {
        ["period"] = period,
        ["currency"] = currency
      },
      FilterGroups = filterGroups,
      Kpis =
      [
        new ReportKpiDto { Label = L["Customer Orders"], Value = totalOrders, IsCount = true, Color = UiColor.Blue },
        new ReportKpiDto { Label = L["Revenue"], Value = totalRevenue, Currency = currency, Color = UiColor.Emerald },
        new ReportKpiDto { Label = L["Paid"], Value = totalPaid, Currency = currency, Color = UiColor.Teal },
        new ReportKpiDto { Label = L["Collection rate"], Value = collectionRate, IsPercent = true, Color = UiColor.Amber }
      ],
      Charts =
      [
        new ReportChartDto
        {
          Key = "monthly-sales",
          Title = L["Monthly sales & payments"],
          Kind = ReportChartKind.Bar,
          Currency = currency,
          Span = 8,
          Categories = [.. monthly.Select(m => m.Label)],
          Series =
          [
            new ReportSeriesDto { Name = L["Revenue"], Kind = ReportChartKind.Bar, Color = UiColor.Blue, Data = [.. monthly.Select(m => m.Revenue)] },
            new ReportSeriesDto { Name = L["Paid"], Kind = ReportChartKind.Bar, Color = UiColor.Emerald, Data = [.. monthly.Select(m => m.Paid)] }
          ]
        },
        new ReportChartDto
        {
          Key = "orders-by-status",
          Title = L["Orders by status"],
          Kind = ReportChartKind.Pie,
          Span = 4,
          Slices = byStatus
        }
      ],
      Table = new ReportTableDto
      {
        Key = "top-orders",
        Title = L["Recent customer orders"],
        Searchable = true,
        Sortable = true,
        Columns =
        [
          new ReportColumnDto { Key = "number", Label = L["Number"], Kind = ReportColumnKind.Text, Width = 120 },
          new ReportColumnDto { Key = "date", Label = L["Date"], Kind = ReportColumnKind.Date, Width = 120 },
          new ReportColumnDto { Key = "customer", Label = L["Customer"], Kind = ReportColumnKind.Text, Width = 220 },
          new ReportColumnDto { Key = "status", Label = L["Status"], Kind = ReportColumnKind.Badge, Width = 150 },
          new ReportColumnDto { Key = "amount", Label = L["Amount"], Kind = ReportColumnKind.Money, Currency = currency, Width = 140 },
          new ReportColumnDto { Key = "paid", Label = L["Paid"], Kind = ReportColumnKind.Money, Currency = currency, Width = 140 }
        ],
        Rows = topRows
      }
    };
  }
}
