import { Component, computed, DestroyRef, inject, linkedSignal } from '@angular/core';
import { RECORD_AUTOCOMPLETE, RECORD_NAME } from '../../controls/record-autocomplete';
import { TextsService } from '../../core/texts.service';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { GRID_HEADER_CLEAR, GRID_HEADER_HOST, GRID_HEADER_STRIP } from '../../theme/css-classes';
import type { GridHeaderDefaultModel } from '../../ui-components';
import { GridCellBase } from '../cell/grid-cell-base';
import { columnLabelOf, columnTitleOf } from '../column/grid-column-label';
import { defaultOperatorFor, operatorOf } from '../filter/grid-filter-operators';
import { type GridFilter, isEmptyFilter, rulesOf } from '../filter/grid-filter-set';
import { typedFilterValue } from '../filter/grid-filter-value';
import { injectClauseText } from './grid-header-clause';
import { GridHeaderMenuDirective } from './grid-header-menu.directive';
import { GridHeaderSortComponent } from './grid-header-sort.component';

const DEBOUNCE_MS = 400;

/**
 * The standard header: the label as the placeholder of a search box that
 * filters the column by containment a moment after typing stops — the text marked while it holds
 * a value, a control to clear it beside the sort control, in the pinned group so
 * nothing covers it; a filter the menu made is shown in the box as its clause,
 * `>= 10`, read only until cleared — a control that cycles the sort, with shift by several
 * columns, and a menu that opens the column's filter overlay. The controls take the header's full height and appear a moment after
 * the pointer enters, so a pass over the headers does not flicker; they stay while the column is
 * sorted or filtered, or while one of them has focus.
 */
@Component({
  selector: 'ui-grid-header-default',
  imports: [GridHeaderMenuDirective, GridHeaderSortComponent, UiIconDirective],
  template: `
    <input
      type="search"
      autocomplete="${RECORD_AUTOCOMPLETE}"
      name="${RECORD_NAME}"
      class="peer flex-1 min-w-0 h-full pl-2 pr-0 bg-transparent text-sm font-semibold tabular-nums outline-none placeholder:text-muted placeholder:font-semibold truncate [&::-webkit-search-cancel-button]:hidden"
      [class]="(hasText() ? 'text-mark' : 'text-ink') + (showsControls() ? (hasText() ? ' pr-16' : ' pr-12') : '')"
      [placeholder]="label()"
      [title]="title()"
      [attr.aria-label]="label()"
      [disabled]="!canType()"
      [readonly]="showsClause()"
      [value]="text()"
      (input)="onInput($event)"
      (keydown.enter)="applyNow()"
      (keydown.escape)="clearOnEscape($event)"
      (click)="$event.stopPropagation()"
    />

    <!-- Over the end of the box, on the header's ground, so a narrow column keeps its label until the pointer arrives;
         the clear control lives here too, so it is never under the group while the box holds a value -->
    @if (hasText() || isSortable() || isFilterable()) {
      <div class="${GRID_HEADER_STRIP}" [class.opacity-100]="showsControls()">
        @if (hasText()) {
          <button type="button" class="${GRID_HEADER_CLEAR}" [attr.aria-label]="texts.of('ClearSearchIn', label())" (click)="clear()">
            <i icon="close" aria-hidden="true"></i>
          </button>
        }
        @if (isSortable()) {
          <ui-grid-header-sort [column]="column()" />
        }
        @if (isFilterable()) {
          <button [uiGridHeaderMenu]="column()" [class.text-mark]="hasFilter()">
            <i icon="ellipsis-vertical" aria-hidden="true"></i>
          </button>
        }
      </div>
    }
  `,
  host: { class: GRID_HEADER_HOST },
})
export class GridHeaderDefaultComponent extends GridCellBase<GridHeaderDefaultModel> {
  protected readonly texts = inject(TextsService);
  private readonly clauseOf = injectClauseText();
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected readonly label = computed(() => columnLabelOf(this.column()));
  protected readonly title = computed(() => columnTitleOf(this.column()));
  protected readonly isSortable = computed(() => this.column().isSortable && !!this.grid);
  protected readonly isFilterable = computed(() => this.column().isFilterable && !!this.grid);

  /**
   * The box writes one operator, `matches` for a text; a column the server sent without it keeps its menu and takes no
   * typing, and so does a checkbox column, whose yes and no are no words to type.
   */
  protected readonly canType = computed(
    () => this.isFilterable() && this.column().type !== 'checkbox' && !!operatorOf(this.column(), defaultOperatorFor(this.column().type)),
  );

  protected readonly sort = computed(() => this.grid?.sortOf(this.column().path) ?? null);

  private readonly filter = computed(() => this.grid?.columnFilterOf(this.column().path));

  protected readonly hasFilter = computed(() => !!this.filter());

  /** The controls stay while the column is sorted or filtered or the box holds a value, and the box leaves them room. */
  protected readonly showsControls = computed(() => this.sort() !== null || this.hasFilter() || this.hasText());

  /** Whether the box shows a filter the menu made — the operator's symbol and the value — rather than what was typed. */
  protected readonly showsClause = computed(() => {
    const filter = this.filter();
    if (!filter) {
      return false;
    }
    const rules = rulesOf(filter);
    return rules.length > 1 || rules[0]?.operator !== defaultOperatorFor(this.column().type);
  });

  /**
   * What the box shows: the filter's value while its operator is the
   * one the box writes, its clause otherwise, and what was typed since.
   */
  protected readonly text = linkedSignal<string>(() => {
    const filter = this.filter();
    if (!filter) {
      return '';
    }
    if (this.showsClause()) {
      return this.clauseOf(filter, this.column());
    }
    const value = rulesOf(filter)[0]?.value;
    return value === null || value === undefined ? '' : String(value);
  });

  protected readonly hasText = computed(() => this.text() !== '');

  constructor() {
    super();
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  protected onInput(event: Event): void {
    this.text.set((event.target as HTMLInputElement).value);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.applyNow(), DEBOUNCE_MS);
  }

  protected applyNow(): void {
    clearTimeout(this.timer);
    const column = this.column();
    // The text of a box that shows the menu's filter is its formula, nothing typed: Enter and a pause's end apply none of it.
    if (!this.grid || !column.path || this.showsClause()) {
      return;
    }

    const rule: GridFilter = {
      kind: 'FilterRule',
      field: column.path,
      operator: defaultOperatorFor(column.type),
      value: typedFilterValue(column.type, this.text()),
    };
    if (isEmptyFilter(rule)) {
      this.grid.clearColumnFilter(column.path);
    } else {
      this.grid.setColumnFilter(rule);
    }
  }

  protected clear(): void {
    this.text.set('');
    clearTimeout(this.timer);
    const path = this.column().path;
    if (this.grid && path) {
      this.grid.clearColumnFilter(path);
    }
  }

  /** `Escape` clears a box that holds a text, and says it handled the key, so no overlay closes with it. */
  protected clearOnEscape(event: Event): void {
    if (this.hasText()) {
      event.preventDefault();
      this.clear();
    }
  }
}
