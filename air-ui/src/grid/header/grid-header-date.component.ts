import { Component, computed, ElementRef, inject, Injector, viewChild } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import type { GridHeaderDateModel } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { OverlayService } from '../../overlay/overlay.service';
import { GRID_HEADER_CLEAR, GRID_HEADER_HOST, GRID_HEADER_STRIP } from '../../theme/css-classes';
import { GridCellBase } from '../cell/grid-cell-base';
import { columnLabelOf, columnTitleOf } from '../column/grid-column-label';
import { temporalTypeOf } from '../column/grid-column-temporal-type';
import { rangeOfFilter, rangeText } from '../filter/grid-date-periods';
import { gridDateRangeOverlay } from '../filter/grid-date-range-overlay.component';
import { injectClauseText } from './grid-header-clause';
import { GridHeaderMenuDirective } from './grid-header-menu.directive';
import { GridHeaderSortComponent } from './grid-header-sort.component';

/**
 * The header of a date or a stamp column, drawn by Air's roles on the table's `surface-2` as every header, with no
 * ground or ring of its own: a button reading the label in the `link` hue — its full text as its title — that opens the
 * date range menu with From, To and the periods around today. While the column is filtered the button reads the filter
 * in the `mark` hue: a range as `01.09.2026 – 30.09.2026`, one date for one day, any other clause as its text. Beside
 * it a quiet control clears the filter, the sort control, and a menu that opens the column's filter overlay for the
 * other operators. A column that does not filter keeps the label and the sort.
 */
@Component({
  selector: 'ui-grid-header-date',
  imports: [GridHeaderMenuDirective, GridHeaderSortComponent, UiIconDirective],
  template: `
    <button
      #anchor
      type="button"
      class="flex-1 min-w-0 h-full pl-2 pr-1 flex items-center text-sm font-semibold tabular-nums text-left outline-none"
      [class]="labelClasses()"
      [class.pr-16]="showsControls()"
      [title]="title()"
      [attr.aria-label]="name()"
      [attr.aria-haspopup]="canPick() ? 'dialog' : null"
      [attr.aria-expanded]="canPick() ? rangeMenu.isOpen() : null"
      [attr.aria-controls]="canPick() ? rangeMenu.id() : null"
      [disabled]="!canPick()"
      (click)="open($event)"
    >
      <span class="truncate">{{ text() || label() }}</span>
    </button>

    @if (hasFilter() || isSortable() || canPick()) {
      <div class="${GRID_HEADER_STRIP}" [class.opacity-100]="showsControls()">
        @if (hasFilter()) {
          <button type="button" class="${GRID_HEADER_CLEAR}" [attr.aria-label]="texts.of('ClearFilterOf', label())" (click)="clear($event)">
            <i icon="close" aria-hidden="true"></i>
          </button>
        }
        @if (isSortable()) {
          <ui-grid-header-sort [column]="column()" />
        }
        @if (canPick()) {
          <button [uiGridHeaderMenu]="column()"><i icon="ellipsis-vertical" aria-hidden="true"></i></button>
        }
      </div>
    }
  `,
  host: { class: GRID_HEADER_HOST },
})
export class GridHeaderDateComponent extends GridCellBase<GridHeaderDateModel> {
  private readonly overlay = inject(OverlayService);
  protected readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService).current;
  private readonly zone = inject(LocaleService).zone;
  private readonly injector = inject(Injector);
  private readonly anchor = viewChild.required<ElementRef<HTMLElement>>('anchor');
  private readonly clauseOf = injectClauseText();

  /** The date range menu the button opens: what the button says of it. */
  protected readonly rangeMenu = this.overlay.menu();

  protected readonly label = computed(() => columnLabelOf(this.column()));
  protected readonly title = computed(() => columnTitleOf(this.column()));
  protected readonly isSortable = computed(() => this.column().isSortable && !!this.grid);
  protected readonly canPick = computed(() => this.column().isFilterable && !!this.column().path && !!this.grid);
  protected readonly sort = computed(() => this.grid?.sortOf(this.column().path) ?? null);

  private readonly filter = computed(() => this.grid?.columnFilterOf(this.column().path));

  protected readonly hasFilter = computed(() => !!this.filter());

  /** What the button reads of the column's filter: its range as `from – to`, any other clause as its text; empty for none. */
  protected readonly text = computed(() => {
    const filter = this.filter();
    if (!filter) {
      return '';
    }
    const column = this.column();
    const range = rangeOfFilter(filter, temporalTypeOf(column), this.zone());
    return range ? rangeText(range, this.locale()) : this.clauseOf(filter, column);
  });

  /** The controls stay while the column is sorted or filtered, and the button leaves them room. */
  protected readonly showsControls = computed(() => this.sort() !== null || this.hasFilter());

  protected readonly labelClasses = computed(() => labelClassesOf(this.canPick(), this.hasFilter()));

  /** The button's name: what it chooses, and the filter it reads while there is one. */
  protected readonly name = computed(() => {
    const choose = this.texts.of('ChoosePeriodOf', this.label());
    return this.hasFilter() ? [choose, this.text()].join(', ') : choose;
  });

  protected open(event: MouseEvent): void {
    event.stopPropagation();
    if (!this.canPick()) {
      return;
    }
    this.rangeMenu.open(
      gridDateRangeOverlay(this.column()),
      this.anchor().nativeElement,
      this.injector,
      this.texts.of('ChoosePeriodOf', this.label()),
    );
  }

  protected clear(event: MouseEvent): void {
    event.stopPropagation();
    const path = this.column().path;
    if (this.grid && path) {
      this.grid.clearColumnFilter(path);
    }
  }
}

/** The button's ink: the `mark` hue over a filter, a link while it opens the menu, the plain ink when it opens nothing. */
function labelClassesOf(canPick: boolean, hasFilter: boolean): string {
  if (!canPick) {
    return 'text-ink cursor-default';
  }
  return hasFilter ? 'text-mark cursor-pointer' : 'text-link hover:text-link-hover cursor-pointer';
}
