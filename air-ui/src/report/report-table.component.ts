import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import { SearchBoxComponent } from '../controls/search-box.component';
import type { ReportColumnDto, ReportFilterItemDto, ReportTableDto, UiColor } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { formatCellAmount, formatCellNumber, moneyFormatOf, percentFormatOf } from '../grid/cell/grid-cell-format';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { UI_COLORS } from '../theme/ui-color';

@Component({
  selector: 'ui-report-table',
  imports: [BtnComponent, SearchBoxComponent, UiIconDirective],
  template: `
    @if (table(); as t) {
      <div class="bg-surface border border-line rounded-xl overflow-hidden flex flex-col shrink-0">
        @if (t.title || t.subtitle || t.searchable || (t.filterChips && t.filterChips.length > 0)) {
          <div class="px-4 py-2.5 border-b border-line bg-surface-2 flex flex-wrap items-center justify-between gap-3">
            <div class="flex flex-col min-w-0">
              @if (t.title; as title) {
                <span class="text-sm font-semibold text-ink truncate">{{ title }}</span>
              }
              @if (t.subtitle; as sub) {
                <span class="text-xs text-muted truncate">{{ sub }}</span>
              }
            </div>
            <div class="flex flex-wrap items-center gap-2">
              @if (t.filterChips && t.filterChips.length > 0) {
                <div class="flex items-center">
                  @for (chip of t.filterChips; track chip.key; let first = $first; let last = $last) {
                    @let active = activeChip() === chip.key;
                    <ui-btn
                      size="small"
                      [label]="chip.label"
                      [color]="chipColor(chip)"
                      [variant]="active ? 'solid' : 'soft'"
                      [attr.aria-pressed]="active"
                      [class]="joinedBtnClass(first, last)"
                      (click)="activeChip.set(active ? 'all' : chip.key)"
                    />
                  }
                </div>
              }
              @if (t.searchable) {
                <ui-search-box class="w-56" [(text)]="searchText" [debounce]="0" />
              }
            </div>
          </div>
        }

        <div class="overflow-x-auto" [class.overflow-y-auto]="!!t.maxHeight" [style.max-height.px]="t.maxHeight ?? null">
          <table class="w-full border-collapse text-sm">
            <thead [class.sticky]="!!t.maxHeight" [class.top-0]="!!t.maxHeight" [class.z-20]="!!t.maxHeight">
              <tr class="bg-surface-2 border-b border-line text-ink font-medium select-none">
                @for (col of t.columns; track col.key) {
                  @let sortable = isColSortable(t, col);
                  <th
                    [class]="headerCellClass(t, col, sortable)"
                    [style.min-width.px]="col.width ?? (col.kind === 'text' ? 160 : 104)"
                    [attr.role]="sortable ? 'button' : null"
                    [attr.tabindex]="sortable ? 0 : null"
                    [attr.aria-sort]="
                      sortable ? (sortKey() === col.key ? (sortDir() === 'asc' ? 'ascending' : 'descending') : 'none') : null
                    "
                    (click)="sortable ? toggleSort(col.key) : null"
                    (keydown.enter)="sortable ? toggleSort(col.key) : null"
                    (keydown.space)="sortable ? ($event.preventDefault(), toggleSort(col.key)) : null"
                  >
                    <div
                      class="inline-flex items-center gap-1"
                      [class.justify-end]="isRightAligned(col)"
                      [class.w-full]="isRightAligned(col)"
                    >
                      <span>{{ col.label }}</span>
                      @if (sortable) {
                        @if (sortKey() === col.key) {
                          <i
                            [icon]="sortDir() === 'asc' ? 'chevron-top' : 'chevron-bottom'"
                            class="flex items-center justify-center w-3.5 h-3.5 text-ink shrink-0"
                          ></i>
                        } @else {
                          <i
                            icon="chevrons-up-down"
                            class="flex items-center justify-center w-3.5 h-3.5 text-muted opacity-50 shrink-0"
                          ></i>
                        }
                      }
                    </div>
                  </th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of displayedRows(); track $index; let idx = $index) {
                <tr [class]="rowClass(row, idx)">
                  @for (col of t.columns; track col.key) {
                    <td [class]="bodyCellClass(t, col, row)">
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
              } @empty {
                <tr>
                  <td [attr.colspan]="t.columns.length" class="h-16 px-4 text-center text-xs text-muted">
                    {{ t.emptyText || texts.of('NoRows') }}
                  </td>
                </tr>
              }
            </tbody>
            @if (t.bottomRows && t.bottomRows.length > 0) {
              <tfoot [class.sticky]="!!t.maxHeight" [class.bottom-0]="!!t.maxHeight" [class.z-20]="!!t.maxHeight">
                @for (row of t.bottomRows; track $index) {
                  <tr class="bg-surface-2 border-t border-line font-semibold">
                    @for (col of t.columns; track col.key) {
                      <td [class]="footerCellClass(t, col, row)">
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
})
export class ReportTableComponent {
  protected readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService).current;

  readonly table = input.required<ReportTableDto>();

  protected readonly searchText = linkedSignal({ source: this.table, computation: () => '' });
  protected readonly activeChip = linkedSignal({ source: this.table, computation: () => 'all' });
  protected readonly sortKey = linkedSignal<ReportTableDto, string | null>({ source: this.table, computation: () => null });
  protected readonly sortDir = linkedSignal<ReportTableDto, 'asc' | 'desc'>({ source: this.table, computation: () => 'asc' });
  protected readonly currentLocale = computed(() => this.locale());

  protected readonly displayedRows = computed<readonly Record<string, unknown>[]>(() => {
    const t = this.table();
    let rows = (t.rows ?? []) as Record<string, unknown>[];

    const chip = this.activeChip();
    if (chip && chip !== 'all' && t.filterField) {
      const field = t.filterField;
      rows = rows.filter(r => String(r[field] ?? '').toLowerCase() === chip.toLowerCase());
    }

    const q = this.searchText().trim().toLowerCase();
    if (q) {
      const cols = t.columns;
      rows = rows.filter(r =>
        cols.some(c => {
          const val = r[c.key];
          return val !== null && val !== undefined && String(val).toLowerCase().includes(q);
        }),
      );
    }

    const sKey = this.sortKey();
    if (sKey) {
      const dir = this.sortDir() === 'asc' ? 1 : -1;
      rows = [...rows].sort((a, b) => compareValues(a[sKey], b[sKey]) * dir);
    }

    return rows;
  });

  protected isColSortable(t: ReportTableDto, col: ReportColumnDto): boolean {
    return col.sortable ?? t.sortable ?? false;
  }

  protected toggleSort(key: string): void {
    if (this.sortKey() !== key) {
      this.sortKey.set(key);
      this.sortDir.set('asc');
      return;
    }
    if (this.sortDir() === 'asc') {
      this.sortDir.set('desc');
    } else {
      this.sortKey.set(null);
    }
  }

  protected chipColor(chip: ReportFilterItemDto): UiColor {
    return asUiColor(chip.color) ?? 'sky';
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

  protected isRightAligned(col: ReportColumnDto): boolean {
    if (col.align) {
      return col.align === 'right';
    }
    return col.kind === 'money' || col.kind === 'number' || col.kind === 'percent';
  }

  protected alignClass(col: ReportColumnDto, isBody = false): string {
    if (col.align === 'center') {
      return 'text-center';
    }
    if (col.align === 'right') {
      return 'text-right tabular-nums';
    }
    if (col.align === 'left') {
      return isBody && col.kind === 'text' ? 'text-left font-medium' : 'text-left';
    }
    if (col.kind === 'badge') {
      return 'text-center';
    }
    if (col.kind === 'text' || col.kind === 'date' || col.kind === 'datetime') {
      return isBody && col.kind === 'text' ? 'text-left font-medium' : 'text-left';
    }
    return 'text-right tabular-nums';
  }

  protected rowClass(row: Record<string, unknown>, idx: number): string {
    const rowHue = asUiColor(row['rowColor'] as string | undefined);
    if (rowHue) {
      return `hue-${rowHue} bg-(--hue-soft)/35 hover:bg-(--hue-soft)/55`;
    }
    return idx % 2 === 0 ? 'bg-row-stripe hover:bg-row-hover' : 'bg-surface hover:bg-row-hover';
  }

  protected badgePillClass(col: ReportColumnDto, row: Record<string, unknown>): string {
    const cellColor = (row[`${col.key}Color`] as string | undefined) ?? (row['color'] as string | undefined);
    const hue = asUiColor(cellColor) ?? 'slate';
    return `inline-flex items-center px-2 py-0.5 rounded-md text-xs font-semibold hue-${hue} bg-(--btn-ground) text-(--btn-ink) border border-(--btn-edge)`;
  }

  protected headerCellClass(t: ReportTableDto, col: ReportColumnDto, sortable: boolean): string {
    const align = this.alignClass(col, false);
    const pad = t.compact || t.columns.length > 10 ? 'px-2 text-xs h-8' : 'px-3 h-9';
    const cursor = sortable ? 'cursor-pointer hover:bg-row-hover' : '';
    const sticky =
      col.sticky === 'left'
        ? 'sticky left-0 z-10 bg-surface-2'
        : col.sticky === 'right'
          ? 'sticky right-0 z-10 bg-surface-2 border-l border-line'
          : '';
    return `${pad} border-r border-line whitespace-nowrap ${align} ${cursor} ${sticky}`;
  }

  protected bodyCellClass(t: ReportTableDto, col: ReportColumnDto, row: Record<string, unknown>): string {
    const align = this.alignClass(col, true);
    const pad = t.compact || t.columns.length > 10 ? 'px-2 text-xs h-8' : 'px-3 h-9';
    const sticky =
      col.sticky === 'left'
        ? 'sticky left-0 z-10 bg-inherit'
        : col.sticky === 'right'
          ? 'sticky right-0 z-10 bg-inherit border-l border-line font-medium'
          : '';
    const cellColor =
      (row[`${col.key}Color`] as string | undefined) ??
      (col.kind !== 'text' && col.kind !== 'badge' ? (row['color'] as string | undefined) : undefined);
    const hue = asUiColor(cellColor);
    const colorCls = hue ? `hue-${hue} text-(--hue-ink) font-semibold` : 'text-ink';
    return `${pad} border-r border-b border-line whitespace-nowrap ${align} ${sticky} ${colorCls}`;
  }

  protected footerCellClass(t: ReportTableDto, col: ReportColumnDto, row: Record<string, unknown>): string {
    const align = this.alignClass(col, false);
    const pad = t.compact || t.columns.length > 10 ? 'px-2 text-xs h-8' : 'px-3 h-9';
    const sticky =
      col.sticky === 'left'
        ? 'sticky left-0 z-10 bg-surface-2'
        : col.sticky === 'right'
          ? 'sticky right-0 z-10 bg-surface-2 border-l border-line'
          : '';
    const cellColor =
      (row[`${col.key}Color`] as string | undefined) ?? (col.kind !== 'text' ? (row['color'] as string | undefined) : undefined);
    const hue = asUiColor(cellColor);
    const colorCls = hue ? `hue-${hue} text-(--hue-ink)` : 'text-ink';
    return `${pad} border-r border-t border-line whitespace-nowrap ${align} ${sticky} ${colorCls}`;
  }

  protected formatCell(col: ReportColumnDto, row: Record<string, unknown>): string {
    const raw = row[col.key];
    if (raw === null || raw === undefined || raw === '') {
      return '';
    }
    if (typeof raw === 'string' && (col.kind === 'text' || col.kind === 'badge' || col.kind === 'date' || col.kind === 'datetime')) {
      return raw;
    }
    const loc = this.currentLocale();
    const decimals = col.decimals ?? (col.kind === 'percent' ? 1 : 0);
    if (col.kind === 'money') {
      const currency = (row[`${col.key}Currency`] as string | undefined) ?? (row['currency'] as string | undefined) ?? col.currency;
      return currency ? formatCellAmount(raw, loc, moneyFormatOf({ decimals, currency })) : formatCellNumber(raw, loc, decimals, true);
    }
    if (col.kind === 'number') {
      return formatCellNumber(raw, loc, decimals, true);
    }
    if (col.kind === 'percent') {
      return formatCellAmount(raw, loc, percentFormatOf({ decimals }));
    }
    return String(raw);
  }
}

function compareValues(a: unknown, b: unknown): number {
  if (a === b) {
    return 0;
  }
  if (a === null || a === undefined || a === '') {
    return 1;
  }
  if (b === null || b === undefined || b === '') {
    return -1;
  }
  if (typeof a === 'number' && typeof b === 'number') {
    return a - b;
  }
  const numA = Number(a);
  const numB = Number(b);
  if (!Number.isNaN(numA) && !Number.isNaN(numB)) {
    return numA - numB;
  }
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
}

export function asUiColor(color: string | null | undefined): UiColor | null {
  if (!color) {
    return null;
  }
  const lower = color.toLowerCase();
  return UI_COLORS.find(c => c === lower) ?? null;
}
