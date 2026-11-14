import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { buildEChartsOptions } from '../chart/chart.component';
import { oklchToRgb, resolveChartSemanticColors, resolveHueHex } from '../chart/chart.service';
import { GridService } from '../grid/grid.service';
import { ScreenMutations } from '../screen/screen-mutations';
import { formatReportKpi } from './report-kpi-format';
import { ReportTableComponent } from './report-table.component';
import { ReportComponent } from './report.component';

describe('Report KPI precision, selective mutation reloads, money column alignment, and ECharts theming', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('preserves fractional KPI decimals while keeping integer KPIs compact', () => {
    expect(formatReportKpi({ label: 'Orders', value: 1200, isCount: true }, 'en-US')).toBe('1,200');
    expect(formatReportKpi({ label: 'Avg Basket', value: 12.45 }, 'en-US')).toBe('12.45');
    expect(formatReportKpi({ label: 'Conversion', value: 0.125 }, 'en-US')).toBe('0.125');
    expect(formatReportKpi({ label: 'Revenue', value: 1245.5, currency: 'USD' }, 'en-US')).toContain('1,245.50');
    expect(formatReportKpi({ label: 'Share', value: 18.75, isPercent: true }, 'en-US')).toContain('18.75');
  });

  it('reloads KPI-only ReportComponent on same-module mutations while ignoring unrelated modules and unmatched reloadServices', () => {
    const mutations = new ScreenMutations();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ScreenMutations, useValue: mutations }],
    });
    const http = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(ReportComponent);
    fixture.componentRef.setInput('model', {
      kind: 'Report',
      service: 'ecom/sales-report',
      Title: 'Sales Report',
    });
    fixture.componentRef.setInput('data', {
      filterGroups: [],
      activeFilters: {},
      hasDateRange: false,
      kpis: [{ label: 'Avg', value: 12.45 }],
      charts: [],
      tables: [],
    });
    fixture.detectChanges();

    // Unrelated module mutation -> zero HTTP reloads
    const unrelatedOp = mutations.begin();
    mutations.publish(unrelatedOp, {
      service: 'org/department-form',
      affected: [{ service: 'org/department-grid', id: 1 }],
      operation: 'save',
    });
    http.expectNone('/api/ecom/sales-report/get-data');

    // Same-module mutation on KPI-only report -> reloads report!
    const sameModuleOp = mutations.begin();
    mutations.publish(sameModuleOp, {
      service: 'ecom/customer-order-form',
      affected: [{ service: 'ecom/customer-order-grid', id: 10 }],
      operation: 'save',
    });
    http.expectOne('/api/ecom/sales-report/get-data').flush({
      data: {
        filterGroups: [],
        activeFilters: {},
        hasDateRange: false,
        kpis: [{ label: 'Avg', value: 14.2 }],
        charts: [],
        tables: [],
      },
    });

    // Unrelated reloadServices -> zero HTTP reloads
    GridService.reloadServices(['org/department-grid']);
    http.expectNone('/api/ecom/sales-report/get-data');

    // Matching reloadServices -> triggers report reload
    GridService.reloadServices(['ecom/sales-report']);
    http.expectOne('/api/ecom/sales-report/get-data').flush({
      data: {
        filterGroups: [],
        activeFilters: {},
        hasDateRange: false,
        kpis: [{ label: 'Avg', value: 15.75 }],
        charts: [],
        tables: [],
      },
    });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('15.75');
    http.verify();
  });

  it('aligns money column decimals consistently across all rows when any cell in the column has cents', () => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(ReportTableComponent);
    fixture.componentRef.setInput('table', {
      title: 'Invoices',
      columns: [
        { key: 'wholeTotal', label: 'Whole', kind: 'money', currency: 'USD' },
        { key: 'exactTotal', label: 'Exact', kind: 'money', currency: 'USD' },
      ],
      rows: [{ cells: { wholeTotal: 100, exactTotal: 100 } }, { cells: { wholeTotal: 250, exactTotal: 100.5 } }],
    });
    fixture.detectChanges();

    const cells = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody td')].map(td => td.textContent?.trim() ?? '');
    expect(cells[0]).toBe('$100');
    expect(cells[1]).toBe('$100.00');
    expect(cells[2]).toBe('$250');
    expect(cells[3]).toBe('$100.50');
  });

  it('honors ReportChartDto.currency and resolves semantic CSS custom properties in ECharts options', () => {
    expect(oklchToRgb('oklch(0.623 0.214 259.815)')).toMatch(/^rgb\(\d+, \d+, \d+\)$/);

    const fakeStyle = {
      getPropertyValue: (prop: string) => {
        const map: Record<string, string> = {
          '--panel-ink': 'rgb(15, 23, 42)',
          '--panel-muted': 'rgb(100, 116, 139)',
          '--panel-line': 'rgb(226, 232, 240)',
          '--panel-surface': 'rgb(255, 255, 255)',
          '--panel-field-edge': 'rgb(203, 213, 225)',
          '--color-emerald-500': 'oklch(0.696 0.17 162.48)',
        };
        return map[prop] ?? '';
      },
    } as unknown as CSSStyleDeclaration;

    const colors = resolveChartSemanticColors(false, fakeStyle);
    expect(colors.text).toBe('rgb(15, 23, 42)');
    expect(resolveHueHex('emerald', false, 0, fakeStyle)).toMatch(/^rgb\(/);

    const opts = buildEChartsOptions(
      {
        key: 'rev',
        title: 'Monthly Revenue',
        kind: 'bar',
        currency: 'USD',
        categories: ['Jan'],
        series: [{ name: 'Net', kind: 'bar', data: [1500], color: 'emerald' }],
      },
      false,
      'en-US',
      fakeStyle,
    ) as Record<string, unknown>;

    const yAxis = opts['yAxis'] as { axisLabel: { formatter: (v: number) => string } };
    expect(yAxis.axisLabel.formatter(1500)).toContain('$');

    const tooltip = opts['tooltip'] as { valueFormatter: (v: unknown) => string };
    expect(tooltip.valueFormatter(1234.5)).toContain('$1,234.50');
  });
});
