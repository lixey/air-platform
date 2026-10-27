import { Component, computed, inject, input, viewChild } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import type { GridModel } from '../core/generated/contracts';
import { valueAt } from '../core/json';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { NavComponent } from '../nav/nav.component';
import { OverlayRef } from '../overlay/overlay-ref';
import { SelectService } from '../select/select.service';
import { formatCellValue, formatCount } from './cell/grid-cell-format';
import { conjunctsOf, fieldOf, type GridFilter } from './filter/grid-filter-set';
import { chipLabel, connectorOf } from './filter/grid-filter-text';
import { GridDeleteOutcomeComponent } from './grid-delete-outcome.component';
import { GridPagerComponent } from './grid-pager.component';
import { GridService } from './grid.service';
import { GridActionsService } from './nav/grid-actions.service';
import { GridTableComponent } from './table/grid-table.component';

/**
 * The `grid` panel: the nav above — the model's `nav`
 * while nothing is selected, with every general filter as a chip after its elements (each reference column's pick, and
 * each item of the general tree's root when it joins by AND, the whole tree when it joins by OR); the actions
 * nav `actionNav` in its place while rows are selected, reading `n / total` in the session's culture — the virtual
 * table filling, and the pager floating over the table's bottom-right corner. The list of a select draws no nav and
 * no pager; the list of a picker draws the model's `nav` with its chips, then the picker's label and, at the end, the
 * control that closes the list, and never the actions nav. It provides
 * the service, so the nav's elements, the cells and the overlays opened from here act on
 * the one grid, and the run of its actions, which therefore outlives the button or the menu item
 * that started one. The page that came with the screen is handed to the service as the first
 * answer, so opening a grid is one request.
 */
@Component({
  selector: 'ui-grid',
  imports: [NavComponent, BtnComponent, UiIconDirective, GridTableComponent, GridPagerComponent, GridDeleteOutcomeComponent],
  providers: [GridService, GridActionsService],
  template: `
    @if (grid.model(); as model) {
      @if (grid.isPicking() && !grid.isPickerList()) {
        <!-- A select's list has no nav: the select's own bar stands above it -->
      } @else if (grid.isPickerList() || grid.selection.isEmpty()) {
        <ui-nav [model]="model.nav" class="shrink-0 border-b border-line bg-surface">
          @for (chip of chips(); track chip.key; let first = $first) {
            <ui-btn
              color="green"
              variant="solid"
              [label]="chip.label"
              [title]="chip.label"
              class="max-w-64 rounded-none! -ml-px"
              [class.rounded-l-lg!]="first"
              [aria-label]="texts.of('RemoveFilterNamed', chip.label)"
              (click)="chip.remove()"
            >
              <i icon="close" aria-hidden="true"></i>
            </ui-btn>
          }
          @if (chips().length > 0) {
            <ui-btn
              color="green"
              icon="close"
              class="rounded-l-none! -ml-px"
              [title]="texts.of('RemoveAllFilters')"
              (click)="grid.clearFilters()"
            />
          }
          @if (grid.isPickerList()) {
            <!-- What is being picked, as a select's bar says it -->
            <span class="min-w-0 truncate px-2 text-sm text-muted">{{ select?.model()?.label }}</span>
          }
          @if (grid.isPickerList()) {
            <ui-btn end icon="close" variant="ghost" color="slate" [title]="texts.of('Close')" (click)="closeList()" />
          }
        </ui-nav>
      } @else {
        <ui-nav [model]="model.actionNav" class="shrink-0 border-b border-line bg-action-nav">
          <span
            start
            class="flex items-center gap-1 px-2 text-sm tabular-nums"
            [attr.aria-label]="texts.of('SelectedOf', selected(), total())"
          >
            <span class="text-mark">{{ selected() }}</span>
            <span class="text-muted">/</span>
            <span class="text-muted">{{ total() }}</span>
          </span>
          @for (sum of selectionSums(); track sum.key) {
            <span
              [attr.title]="sum.label"
              [attr.aria-label]="sum.label ? sum.label + ': ' + sum.text : sum.text"
              [class]="
                'hue-' +
                sum.hue +
                ' inline-flex items-center gap-1.5 min-h-9 px-2.5 rounded-lg border border-(--btn-edge) bg-(--badge-ground) text-(--badge-ink) text-sm font-semibold tabular-nums shrink-0'
              "
            >
              <span class="font-medium">Σ:</span>
              <span>{{ sum.text }}</span>
            </span>
          }
        </ui-nav>
      }
    }
    <ui-grid-delete-outcome />
    <div class="relative flex flex-col flex-1 min-h-0 min-w-0">
      <ui-grid-table [paged]="!grid.isPicking()" />
      @if (!grid.isPicking()) {
        <ui-grid-pager [inset]="table()?.bottomInset() ?? 0" />
      }
    </div>
  `,
  host: { class: 'flex flex-col w-full h-full min-w-0 min-h-0 overflow-hidden' },
})
export class GridComponent {
  protected readonly grid = inject(GridService);
  protected readonly texts = inject(TextsService);
  /** The select the grid is the list of, and the overlay it stands in: what the header of a picker's list reads. */
  protected readonly select = inject(SelectService, { optional: true });
  private readonly overlayRef = inject(OverlayRef, { optional: true });
  private readonly locale = inject(LocaleService).current;
  private readonly zone = inject(LocaleService).zone;

  protected readonly table = viewChild(GridTableComponent);

  /** The grid model, as the `grid` panel carries it. */
  readonly model = input<GridModel | null>();

  /** The screen's data: the first page. */
  readonly data = input<unknown>();

  /**
   * A chip per reference column's pick, then one per filter the general tree's AND joins, a group read in parentheses:
   * a tree joined by OR is one chip.
   */
  protected readonly chips = computed(() => {
    const columns = this.grid.allColumns();
    const connector = connectorOf(this.texts);
    const labelOf = (filter: GridFilter) => chipLabel(filter, columns, connector, this.locale(), this.zone());
    const picks = this.grid.filterPicks().map(filter => ({
      key: `pick:${fieldOf(filter)}`,
      label: labelOf(filter),
      remove: () => this.grid.removeFilter(fieldOf(filter)),
    }));
    return [
      ...picks,
      ...conjunctsOf(this.grid.filterTree()).map((item, index) => ({
        key: `item:${index}`,
        label: labelOf(item),
        remove: () => this.grid.removeFilterItem(index),
      })),
    ];
  });

  /** The action nav's `n / total`, counted in the session's culture. */
  protected readonly selected = computed(() => formatCount(this.grid.selection.count(), this.locale()));
  protected readonly total = computed(() => formatCount(this.grid.total(), this.locale()));

  /** Sums over the selected rows for columns declaring `selectionSumUnit`, shown in `actionNav` after `Export`. */
  protected readonly selectionSums = computed(() => {
    const rows = this.grid.selection.selected();
    if (rows.length === 0) {
      return [];
    }
    const locale = this.locale();
    const zone = this.zone();
    return this.grid
      .allColumns()
      .filter(col => col.selectionSumUnit !== undefined && col.selectionSumUnit !== null && !!col.path)
      .map(col => {
        const path = col.path!;
        const colorPath = col.cell?.colorPath ?? `${path}Color`;
        const excludeColor = col.selectionSumExcludeColor ?? null;
        const primaryRows = excludeColor ? rows.filter(r => valueAt(r, colorPath) !== excludeColor) : rows;
        const activeRows = primaryRows.length > 0 ? primaryRows : rows;
        const isExcludedOnly = excludeColor !== null && primaryRows.length === 0;
        let sum = 0;
        for (const r of activeRows) {
          const val = valueAt(r, path);
          const num = typeof val === 'number' ? val : Number(val);
          if (Number.isFinite(num)) {
            sum += num;
          }
        }
        const formatted = formatCellValue(sum, col, locale, zone);
        const unit = col.selectionSumUnit ? ` ${col.selectionSumUnit}` : '';
        const hue = isExcludedOnly && excludeColor ? excludeColor : sum < 0 ? 'rose' : sum > 0 ? 'emerald' : 'slate';
        return {
          key: path,
          label: col.label ?? null,
          hue,
          text: `${formatted}${unit}`,
        };
      });
  });

  constructor() {
    this.grid.bind(this.model, this.data);
  }

  /** The close control of a picker's list: the list's overlay is dismissed, and its host returns focus to the field. */
  protected closeList(): void {
    void this.overlayRef?.dismiss();
  }
}
