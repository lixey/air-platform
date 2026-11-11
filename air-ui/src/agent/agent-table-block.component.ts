import { Component, inject, Injector, input } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import type { ReportColumnDto, ReportTableDto, RowDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { formatCellAmount, formatCellDate, formatCellNumber, moneyFormatOf, percentFormatOf } from '../grid/cell/grid-cell-format';
import { OverlayService } from '../overlay/overlay.service';
import { singlePanelLayout } from '../panels/single-panel-layout';
import { canOpenTarget, openReportRow } from '../report/report-row-open';
import { ScreenMutations } from '../screen/screen-mutations';
import { asAgentUiColor } from './agent-ui-helpers';

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
                <tr
                  [class]="rowClass(idx, row)"
                  [attr.tabindex]="isRowClickable(row) ? 0 : null"
                  (click)="openRow(row, $event)"
                  (keydown.enter)="openRow(row, $event)"
                  (keydown.space)="onRowSpace(row, $event)"
                >
                  @for (col of t.columns; track col.key) {
                    <td [class]="bodyCellClass(col, row)">
                      @if (col.kind === 'badge') {
                        @let cellText = formatCell(col, row);
                        @if (cellText) {
                          <span [class]="badgePillClass(col, row)">
                            {{ cellText }}
                          </span>
                        }
                      } @else {
                        {{ formatCell(col, row) }}
                      }
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
                        {{ formatCell(col, row) }}
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
  private readonly zone = inject(LocaleService).zone;
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  private readonly mutations = inject(ScreenMutations, { optional: true });
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

  protected isRowClickable(row: RowDto): boolean {
    return canOpenTarget(row.open);
  }

  protected rowClass(idx: number, row: RowDto): string {
    const cursor = this.isRowClickable(row) ? ' cursor-pointer' : '';
    const rowHue = asAgentUiColor(row.color);
    if (rowHue) {
      return `hue-${rowHue} bg-(--hue-cell) hover:bg-(--hue-cell-hover)${cursor}`;
    }
    const base = idx % 2 === 0 ? 'bg-row-stripe hover:bg-row-hover' : 'bg-surface hover:bg-row-hover';
    return `${base}${cursor}`;
  }

  protected onRowSpace(row: RowDto, event: Event): void {
    if (!this.isRowClickable(row)) {
      return;
    }
    event.preventDefault();
    this.openRow(row, event);
  }

  protected openRow(row: RowDto, event: Event): void {
    openReportRow(row, event, this.overlay, this.injector, this.mutations);
  }

  protected badgePillClass(col: ReportColumnDto, row: RowDto): string {
    const cellColor = row.colors?.[col.key] ?? row.color;
    const hue = asAgentUiColor(cellColor) ?? 'slate';
    return `inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-semibold hue-${hue} bg-(--btn-ground) text-(--btn-ink) border border-(--btn-edge)`;
  }

  protected formatCell(col: ReportColumnDto, row: RowDto): string {
    const raw = row.cells[col.key];
    if (raw === null || raw === undefined || raw === '') {
      return '';
    }
    if (typeof raw === 'string' && (col.kind === 'text' || col.kind === 'badge')) {
      return raw;
    }
    const loc = this.locale();
    if (col.kind === 'date' || col.kind === 'datetime') {
      return formatCellDate(raw, col.kind, loc, this.zone());
    }
    if (typeof raw === 'number' || (typeof raw === 'string' && /^[+-]?\d+(?:\.\d+)?$/.test(raw.trim()))) {
      const decimals = col.decimals ?? (col.kind === 'percent' ? 1 : null);
      if (col.kind === 'money') {
        const currency = row.currencies?.[col.key] ?? col.currency;
        return currency
          ? formatCellAmount(raw, loc, moneyFormatOf({ decimals: col.decimals ?? 0, currency }))
          : formatCellNumber(raw, loc, decimals, true);
      }
      if (col.kind === 'number') {
        return formatCellNumber(raw, loc, decimals, true);
      }
      if (col.kind === 'percent') {
        return formatCellAmount(raw, loc, percentFormatOf({ decimals: decimals ?? 1 }));
      }
    }
    return String(raw);
  }

  protected headerCellClass(col: ReportColumnDto): string {
    const isLeft = col.kind === 'text' || col.kind === 'badge' || col.kind === 'date' || col.kind === 'datetime';
    const align = isLeft ? 'text-left' : 'text-right';
    return `h-8 px-2.5 border-r border-line whitespace-nowrap last:border-r-0 ${align}`;
  }

  protected bodyCellClass(col: ReportColumnDto, row: RowDto): string {
    const isLeft = col.kind === 'text' || col.kind === 'badge' || col.kind === 'date' || col.kind === 'datetime';
    const align =
      col.kind === 'text' || col.kind === 'badge' ? 'text-left font-medium' : isLeft ? 'text-left tabular-nums' : 'text-right tabular-nums';
    const cellColor = row.colors?.[col.key] ?? (!isLeft ? row.color : undefined);
    const hue = asAgentUiColor(cellColor);
    const colorCls = hue ? `hue-${hue} text-(--hue-ink)` : 'text-ink';
    return `h-8 px-2.5 border-r border-b border-line whitespace-nowrap last:border-r-0 ${align} ${colorCls}`;
  }

  protected footerCellClass(col: ReportColumnDto, row: RowDto): string {
    const isLeft = col.kind === 'text' || col.kind === 'badge' || col.kind === 'date' || col.kind === 'datetime';
    const align = isLeft ? 'text-left' : 'text-right tabular-nums';
    const cellColor = row.colors?.[col.key] ?? (!isLeft ? row.color : undefined);
    const hue = asAgentUiColor(cellColor);
    const colorCls = hue ? `hue-${hue} text-(--hue-ink)` : 'text-ink';
    return `h-8 px-2.5 border-r border-t border-line whitespace-nowrap last:border-r-0 ${align} ${colorCls}`;
  }
}
