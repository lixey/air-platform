import { Component, computed, inject, Injector, input } from '@angular/core';
import { DomSanitizer, type SafeHtml } from '@angular/platform-browser';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import { ChartComponent } from '../chart/chart.component';
import type { ReportChartDto, ReportColumnDto, ReportKpiDto, ReportTableDto, UiColor } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { formatCellAmount, formatCellNumber, percentFormatOf } from '../grid/cell/grid-cell-format';
import { NavComponent } from '../nav/nav.component';
import { OverlayRef } from '../overlay/overlay-ref';
import { OverlayService } from '../overlay/overlay.service';
import { singlePanelLayout } from '../panels/single-panel-layout';
import { UI_COLORS } from '../theme/ui-color';
import { renderMarkdownToSafeHtml, sanitizeAgentHtml } from './agent-markdown';

function asUiColor(color: string | null | undefined): UiColor | null {
  if (!color) {
    return null;
  }
  const lower = color.toLowerCase();
  return UI_COLORS.find(c => c === lower) ?? null;
}

export interface AgentBlockOverlayModel {
  kind: 'AgentBlockOverlay';
  title: string;
  chart?: ReportChartDto | null;
  table?: ReportTableDto | null;
}

@Component({
  selector: 'ui-agent-markdown-block',
  template: `
    <div
      class="rounded-lg border border-line bg-surface-2 px-3.5 py-2.5 text-sm text-ink break-words space-y-1.5"
      [innerHTML]="safeHtml()"
    ></div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentMarkdownBlockComponent {
  private readonly sanitizer = inject(DomSanitizer);
  readonly content = input.required<string>();

  protected readonly safeHtml = computed<SafeHtml>(() => this.sanitizer.bypassSecurityTrustHtml(renderMarkdownToSafeHtml(this.content())));
}

@Component({
  selector: 'ui-agent-html-block',
  template: `
    <div
      class="rounded-lg border border-line bg-surface-2 px-3.5 py-2.5 text-sm text-ink break-words overflow-x-auto"
      [innerHTML]="safeHtml()"
    ></div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentHtmlBlockComponent {
  private readonly sanitizer = inject(DomSanitizer);
  readonly html = input.required<string>();

  protected readonly safeHtml = computed<SafeHtml>(() => this.sanitizer.bypassSecurityTrustHtml(sanitizeAgentHtml(this.html())));
}

@Component({
  selector: 'ui-agent-kpi-block',
  template: `
    <div class="grid grid-cols-2 gap-2 w-full min-w-0">
      @for (kpi of kpis(); track kpi.label) {
        <div class="bg-surface border border-line rounded-xl p-3 flex flex-col gap-0.5 min-w-0">
          <span class="text-xs font-medium text-muted truncate">{{ kpi.label }}</span>
          <span class="text-lg font-semibold tabular-nums truncate" [class]="hueTextClass(kpi.color)">
            {{ formatKpi(kpi) }}
          </span>
          @if (kpi.subtitle; as sub) {
            <span class="text-[11px] text-muted truncate">{{ sub }}</span>
          }
        </div>
      }
    </div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentKpiBlockComponent {
  private readonly locale = inject(LocaleService).current;
  readonly kpis = input.required<readonly ReportKpiDto[]>();

  protected formatKpi(kpi: ReportKpiDto): string {
    const loc = this.locale();
    if (kpi.isPercent) {
      return formatCellAmount(kpi.value, loc, percentFormatOf({ decimals: 1 }));
    }
    return formatCellNumber(kpi.value, loc, 0, true);
  }

  protected hueTextClass(color: string | null | undefined): string {
    const hue = asUiColor(color);
    return hue ? `hue-${hue} text-(--hue-ink)` : 'text-ink';
  }
}

@Component({
  selector: 'ui-agent-chart-block',
  imports: [ChartComponent, BtnComponent],
  template: `
    <div class="relative w-full min-w-0">
      <ui-chart [chart]="chart()" />
      @if (expandable()) {
        <div class="absolute top-2.5 right-2.5 z-10">
          <ui-btn size="small" variant="ghost" icon="fullscreen" [title]="texts.of('FitToScreen')" (click)="openExpanded()" />
        </div>
      }
    </div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentChartBlockComponent {
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  protected readonly texts = inject(TextsService);

  readonly chart = input.required<ReportChartDto>();
  readonly expandable = input(true);

  protected openExpanded(): void {
    const c = this.chart();
    const expandedChart: ReportChartDto = { ...c, height: 440 };
    this.overlay.open(
      {
        key: `agent.chart.${c.key}`,
        presentation: 'dialog',
        width: '920px',
        closesOnEscape: true,
        closesOnOutside: true,
        layout: singlePanelLayout('main', {
          kind: 'AgentBlockOverlay',
          title: c.title || this.texts.of('AgentTitle'),
          chart: expandedChart,
        }),
      },
      null,
      this.injector,
    );
  }
}

@Component({
  selector: 'ui-agent-table-block',
  imports: [BtnComponent],
  template: `
    @if (table(); as t) {
      <div class="w-full min-w-0 bg-surface border border-line rounded-xl overflow-hidden flex flex-col">
        <div class="flex items-center justify-between gap-2 px-3 py-1.5 border-b border-line bg-surface-2">
          <div class="text-xs font-semibold text-ink truncate">
            {{ t.title || texts.of('RowCount', t.rows.length) }}
          </div>
          @if (expandable()) {
            <ui-btn size="small" variant="ghost" icon="fullscreen" [title]="texts.of('FitToScreen')" (click)="openExpanded()" />
          }
        </div>
        <div class="overflow-x-auto max-h-96">
          <table class="w-full border-collapse text-xs">
            <thead>
              <tr class="bg-surface-2 border-b border-line text-ink font-medium select-none sticky top-0 z-10">
                @for (col of t.columns; track col.key) {
                  <th [class]="headerCellClass(col)">
                    {{ col.label }}
                  </th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of t.rows; track $index; let idx = $index) {
                <tr [class]="idx % 2 === 0 ? 'bg-row-stripe hover:bg-row-hover' : 'bg-surface hover:bg-row-hover'">
                  @for (col of t.columns; track col.key) {
                    <td [class]="bodyCellClass(col, row)">
                      {{ formatCell(col, row[col.key]) }}
                    </td>
                  }
                </tr>
              }
            </tbody>
            @if (t.bottomRows && t.bottomRows.length > 0) {
              <tfoot>
                @for (row of t.bottomRows; track $index) {
                  <tr class="bg-surface-2 border-t border-line font-semibold">
                    @for (col of t.columns; track col.key) {
                      <td [class]="footerCellClass(col, row)">
                        {{ formatCell(col, row[col.key]) }}
                      </td>
                    }
                  </tr>
                }
              </tfoot>
            }
          </table>
        </div>
      </div>
    }
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentTableBlockComponent {
  private readonly locale = inject(LocaleService).current;
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  protected readonly texts = inject(TextsService);

  readonly table = input.required<ReportTableDto>();
  readonly expandable = input(true);

  protected openExpanded(): void {
    const t = this.table();
    this.overlay.open(
      {
        key: 'agent.table.expanded',
        presentation: 'dialog',
        width: '960px',
        closesOnEscape: true,
        closesOnOutside: true,
        layout: singlePanelLayout('main', {
          kind: 'AgentBlockOverlay',
          title: t.title || this.texts.of('AgentTitle'),
          table: t,
        }),
      },
      null,
      this.injector,
    );
  }

  protected formatCell(col: ReportColumnDto, raw: unknown): string {
    if (raw === null || raw === undefined || raw === '') {
      return '';
    }
    if (typeof raw === 'string' && col.kind === 'text') {
      return raw;
    }
    const loc = this.locale();
    if (typeof raw === 'number' || (typeof raw === 'string' && /^[+-]?\d+(?:\.\d+)?$/.test(raw.trim()))) {
      if (col.kind === 'money' || col.kind === 'number') {
        return formatCellNumber(raw, loc, 0, true);
      }
      if (col.kind === 'percent') {
        return formatCellAmount(raw, loc, percentFormatOf({ decimals: 1 }));
      }
    }
    return String(raw);
  }

  protected headerCellClass(col: ReportColumnDto): string {
    const align = col.kind === 'text' ? 'text-left' : 'text-right';
    return `h-8 px-2.5 border-r border-line whitespace-nowrap last:border-r-0 ${align}`;
  }

  protected bodyCellClass(col: ReportColumnDto, row: Record<string, unknown>): string {
    const align = col.kind === 'text' ? 'text-left font-medium' : 'text-right tabular-nums';
    const cellColor =
      (row[`${col.key}Color`] as string | undefined) ?? (col.kind !== 'text' ? (row['color'] as string | undefined) : undefined);
    const hue = asUiColor(cellColor);
    const colorCls = hue ? `hue-${hue} text-(--hue-ink)` : 'text-ink';
    return `h-8 px-2.5 border-r border-b border-line whitespace-nowrap last:border-r-0 ${align} ${colorCls}`;
  }

  protected footerCellClass(col: ReportColumnDto, row: Record<string, unknown>): string {
    const align = col.kind === 'text' ? 'text-left' : 'text-right tabular-nums';
    const cellColor =
      (row[`${col.key}Color`] as string | undefined) ?? (col.kind !== 'text' ? (row['color'] as string | undefined) : undefined);
    const hue = asUiColor(cellColor);
    const colorCls = hue ? `hue-${hue} text-(--hue-ink)` : 'text-ink';
    return `h-8 px-2.5 border-r border-t border-line whitespace-nowrap last:border-r-0 ${align} ${colorCls}`;
  }
}

@Component({
  selector: 'ui-agent-block-overlay',
  imports: [NavComponent, BtnComponent, AgentChartBlockComponent, AgentTableBlockComponent],
  template: `
    @if (model(); as m) {
      <ui-nav class="shrink-0 border-b border-line bg-surface-2">
        <ng-container ngProjectAs="[start]">
          <span class="pl-2 font-semibold text-ink truncate">{{ m.title }}</span>
        </ng-container>
        <ng-container ngProjectAs="[end]">
          <ui-btn variant="ghost" icon="close" [title]="texts.of('Close')" (click)="close()" />
        </ng-container>
      </ui-nav>
      <div class="flex-1 min-h-0 overflow-auto p-4 bg-field">
        @if (m.chart; as c) {
          <ui-agent-chart-block [chart]="c" [expandable]="false" />
        }
        @if (m.table; as t) {
          <ui-agent-table-block [table]="t" [expandable]="false" />
        }
      </div>
    }
  `,
  host: { class: 'flex flex-col w-full h-full min-w-0 min-h-0 overflow-hidden bg-surface' },
})
export class AgentBlockOverlayComponent {
  protected readonly texts = inject(TextsService);
  private readonly overlayRef = inject(OverlayRef, { optional: true });

  readonly model = input<AgentBlockOverlayModel>();

  protected close(): void {
    this.overlayRef?.close();
  }
}
