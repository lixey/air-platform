import { Component, computed, DestroyRef, inject, input, linkedSignal, signal } from '@angular/core';
import type { Subscription } from 'rxjs';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import { ChartComponent } from '../chart/chart.component';
import { Api } from '../core/api';
import type {
  ReportDataDto,
  ReportFilterItemDto,
  ReportKpiDto,
  ReportModel,
  ReportServiceEndpoints,
  UiColor,
} from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { TemporalBoxComponent } from '../datepicker/temporal-box.component';
import { formatCellAmount, formatCellNumber, moneyFormatOf, percentFormatOf } from '../grid/cell/grid-cell-format';
import { GridService } from '../grid/grid.service';
import { ScreenMutations } from '../screen/screen-mutations';
import { asUiColor, ReportTableComponent } from './report-table.component';

@Component({
  selector: 'ui-report',
  imports: [BtnComponent, ChartComponent, ReportTableComponent, TemporalBoxComponent],
  template: `
    @if (reportData(); as report) {
      @if (report.filterGroups.length > 0 || report.hasDateRange) {
        <div class="shrink-0 border-b border-line bg-surface px-3 py-2 flex flex-wrap items-center gap-x-4 gap-y-2">
          @for (group of report.filterGroups; track group.key) {
            <div class="flex items-center gap-1.5" role="group" [attr.aria-label]="group.label">
              <span class="text-xs font-medium text-muted select-none">{{ group.label }}:</span>
              <div class="flex items-center">
                @for (item of group.items; track item.key; let first = $first; let last = $last) {
                  @let active = isFilterActive(report.activeFilters, group.key, item.key, first);
                  <ui-btn
                    [label]="item.label"
                    [color]="itemColor(item)"
                    [variant]="active ? 'solid' : 'soft'"
                    [attr.aria-pressed]="active"
                    [class]="joinedBtnClass(first, last)"
                    (click)="selectFilter(group.key, item.key)"
                  />
                }
              </div>
            </div>
          }
          @if (report.hasDateRange) {
            <div class="flex items-center gap-1.5">
              <span class="text-xs font-medium text-muted select-none">{{ texts.of('Periods') }}:</span>
              <div class="w-34">
                <ui-temporal-box
                  kind="date"
                  temporalType="dateOnly"
                  [clearable]="true"
                  [placeholder]="texts.of('PeriodFrom')"
                  [value]="report.activeFilters['from'] || null"
                  (valueChange)="selectDateFilter('from', $event)"
                />
              </div>
              <span class="text-xs text-muted select-none">—</span>
              <div class="w-34">
                <ui-temporal-box
                  kind="date"
                  temporalType="dateOnly"
                  [clearable]="true"
                  [placeholder]="texts.of('PeriodTo')"
                  [value]="report.activeFilters['to'] || null"
                  (valueChange)="selectDateFilter('to', $event)"
                />
              </div>
              @if (report.activeFilters['from'] || report.activeFilters['to']) {
                <ui-btn icon="close" color="slate" variant="ghost" [title]="texts.of('Clear')" (click)="clearDateRange()" />
              }
            </div>
          }
        </div>
      }

      <div class="flex-1 min-h-0 min-w-0 overflow-auto p-4 flex flex-col gap-4" [class.opacity-60]="isLoading()">
        @if (report.kpis && report.kpis.length > 0) {
          <div class="grid grid-cols-2 @2xl/panel:grid-cols-4 gap-3 shrink-0">
            @for (kpi of report.kpis; track kpi.label) {
              <div class="bg-surface border border-line rounded-xl p-3.5 flex flex-col gap-1 min-w-0">
                <span class="text-xs font-medium text-muted truncate">{{ kpi.label }}</span>
                <span class="text-xl font-semibold tabular-nums truncate" [class]="hueTextClass(kpi.color)">
                  {{ formatKpi(kpi) }}
                </span>
                @if (kpi.subtitle; as sub) {
                  <span class="text-xs text-muted truncate">{{ sub }}</span>
                }
              </div>
            }
          </div>
        }

        @if (report.charts && report.charts.length > 0) {
          <div class="grid grid-cols-1 @3xl/panel:grid-cols-12 gap-4 shrink-0">
            @for (chart of report.charts; track chart.key) {
              <ui-chart [chart]="chart" [class]="chartSpanClass(chart.span, report.charts.length)" />
            }
          </div>
        }

        @if (report.table; as table) {
          <ui-report-table [table]="table" />
        }

        @if (report.tables && report.tables.length > 0) {
          @for (t of report.tables; track t.key ?? $index) {
            <ui-report-table [table]="t" />
          }
        }
      </div>
    }
  `,
  host: { class: 'flex flex-col w-full h-full min-w-0 min-h-0 overflow-hidden' },
})
export class ReportComponent {
  private static readonly liveReports = new Set<ReportComponent>();
  static {
    GridService.onReloadServices(() => ReportComponent.reloadAll());
  }

  static reloadAll(): void {
    for (const report of ReportComponent.liveReports) {
      const current = report.reportData();
      if (current) {
        report.applyFilters(current.activeFilters);
      }
    }
  }

  private readonly api = inject(Api);
  protected readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService).current;
  private readonly destroyRef = inject(DestroyRef);
  private readonly mutations = inject(ScreenMutations, { optional: true });

  readonly model = input<ReportModel>();
  readonly data = input<unknown>();

  private activeSub: Subscription | null = null;
  private unsubscribeMutations: (() => void) | null = null;
  protected readonly isLoading = signal(false);

  protected readonly reportData = linkedSignal<unknown, ReportDataDto | null>({
    source: () => this.data(),
    computation: d => (d as ReportDataDto | null) ?? null,
  });

  protected readonly currentLocale = computed(() => this.locale());

  constructor() {
    ReportComponent.liveReports.add(this);
    this.subscribeMutations();
    this.destroyRef.onDestroy(() => {
      ReportComponent.liveReports.delete(this);
      this.unsubscribeMutations?.();
      this.unsubscribeMutations = null;
      this.activeSub?.unsubscribe();
      this.activeSub = null;
    });
  }

  private subscribeMutations(): void {
    this.unsubscribeMutations?.();
    if (!this.mutations) {
      return;
    }
    this.unsubscribeMutations = this.mutations.subscribe({
      accepts: () => true,
      refresh: () => {
        const current = this.reportData();
        if (current) {
          this.applyFilters(current.activeFilters);
        }
      },
      invalidate: () => this.subscribeMutations(),
    });
  }

  protected isFilterActive(activeFilters: Record<string, string>, groupKey: string, itemKey: string, isFirst: boolean): boolean {
    const val = activeFilters?.[groupKey];
    return val ? val === itemKey : isFirst;
  }

  protected itemColor(item: ReportFilterItemDto): UiColor {
    return asUiColor(item.color) ?? 'sky';
  }

  protected joinedBtnClass(first: boolean, last: boolean): string {
    if (first && last) {
      return '';
    }
    if (first) {
      return 'rounded-r-none!';
    }
    if (last) {
      return 'rounded-l-none! -ml-px';
    }
    return 'rounded-none! -ml-px';
  }

  protected selectFilter(groupKey: string, itemKey: string): void {
    const current = this.reportData();
    if (!current) {
      return;
    }

    const nextFilters: Record<string, string> = {
      ...current.activeFilters,
      [groupKey]: itemKey,
    };

    if (groupKey === 'year' && itemKey !== 'all') {
      delete nextFilters['from'];
      delete nextFilters['to'];
    }

    this.applyFilters(nextFilters);
  }

  protected selectDateFilter(bound: 'from' | 'to', value: string | null): void {
    const current = this.reportData();
    if (!current) {
      return;
    }

    const currentVal = current.activeFilters[bound] || null;
    if (currentVal === (value || null)) {
      return;
    }

    const nextFilters: Record<string, string> = {
      ...current.activeFilters,
    };

    if (value) {
      nextFilters[bound] = value;
      if ('year' in nextFilters) {
        nextFilters['year'] = 'all';
      }
    } else {
      delete nextFilters[bound];
    }

    this.applyFilters(nextFilters);
  }

  protected clearDateRange(): void {
    const current = this.reportData();
    if (!current) {
      return;
    }

    const nextFilters: Record<string, string> = {
      ...current.activeFilters,
    };
    delete nextFilters['from'];
    delete nextFilters['to'];
    this.applyFilters(nextFilters);
  }

  private applyFilters(nextFilters: Record<string, string>): void {
    const current = this.reportData();
    const service = this.model()?.service;
    if (!current || !service) {
      return;
    }

    this.reportData.set({
      ...current,
      activeFilters: nextFilters,
    });

    this.activeSub?.unsubscribe();
    this.isLoading.set(true);
    this.activeSub = this.api
      .postTo<ReportServiceEndpoints, 'get-data'>(service, 'get-data', {
        isDataOnly: true,
        filters: nextFilters,
      })
      .subscribe({
        next: res => {
          this.isLoading.set(false);
          if (res.data) {
            this.reportData.set(res.data as ReportDataDto);
          }
        },
        error: () => {
          this.isLoading.set(false);
        },
      });
  }

  protected formatKpi(kpi: ReportKpiDto): string {
    const loc = this.currentLocale();
    if (kpi.isPercent) {
      return formatCellAmount(kpi.value, loc, percentFormatOf({ decimals: 1 }));
    }
    if (kpi.currency) {
      return formatCellAmount(kpi.value, loc, moneyFormatOf({ decimals: 0, currency: kpi.currency }));
    }
    return formatCellNumber(kpi.value, loc, 0, true);
  }

  protected hueTextClass(color: string | null | undefined): string {
    const hue = asUiColor(color);
    return hue ? `hue-${hue} text-(--hue-ink)` : 'text-ink';
  }

  protected chartSpanClass(span: number | null | undefined, totalCharts: number): string {
    const effective = span ?? (totalCharts === 1 ? 12 : 6);
    if (effective >= 12) {
      return '@3xl/panel:col-span-12';
    }
    if (effective === 7) {
      return '@3xl/panel:col-span-7';
    }
    if (effective === 5) {
      return '@3xl/panel:col-span-5';
    }
    if (effective === 8) {
      return '@3xl/panel:col-span-8';
    }
    if (effective === 4) {
      return '@3xl/panel:col-span-4';
    }
    return '@3xl/panel:col-span-6';
  }
}
