import { Component, computed, DestroyRef, ElementRef, inject, Injector, viewChild } from '@angular/core';
import type { GridHeaderReferenceModel } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { SelectService } from '../../select/select.service';
import { GRID_HEADER_CLEAR, GRID_HEADER_HOST, GRID_HEADER_STRIP } from '../../theme/css-classes';
import { GridCellBase } from '../cell/grid-cell-base';
import { columnLabelOf } from '../column/grid-column-label';
import { selectSourceOf } from '../column/grid-column-select-source';
import { GridHeaderSortComponent } from './grid-header-sort.component';

/**
 * The header of a reference column, drawn by Air's roles on the table's `surface-2` as every header, with no ground or
 * ring of its own: a button reading the label in the `link` hue — its full text as its title, told from a text box as
 * the links in the cells below — and the count of the picked in the `mark` hue, opening the select over the column's
 * filter grid; one pick, or several with `Alt` (`Option`) or `Meta` (`Cmd`) held at the click or at `Enter`, or when
 * more than one is picked already. Beside it a quiet control clears the filter, and the sort control. No text box: the
 * name is searched inside the select. A select column's header opens the same select over the column's options, in
 * the order given and without a request. Without a filter service and without options the button opens nothing; the
 * cell's link still filters.
 */
@Component({
  selector: 'ui-grid-header-reference',
  imports: [GridHeaderSortComponent, UiIconDirective],
  providers: [SelectService],
  template: `
    <button
      #anchor
      type="button"
      class="flex-1 min-w-0 h-full pl-2 pr-1 flex items-center text-sm font-semibold truncate text-left outline-none"
      [class]="labelClasses()"
      [class.pr-12]="showsControls()"
      [title]="label()"
      [attr.aria-label]="name()"
      [attr.aria-haspopup]="canPick() ? 'dialog' : null"
      [attr.aria-expanded]="canPick() ? select.isOpen() : null"
      [disabled]="!canPick()"
      (click)="open($event)"
      (keydown)="onKeyDown($event)"
    >
      <span class="truncate">{{ label() }}</span>
      @if (count() > 0) {
        <span class="ml-0.5 text-mark tabular-nums">{{ count() }}</span>
      }
    </button>

    @if (count() > 0 || isSortable()) {
      <div class="${GRID_HEADER_STRIP}" [class.opacity-100]="showsControls()">
        @if (count() > 0) {
          <button type="button" class="${GRID_HEADER_CLEAR}" [attr.aria-label]="texts.of('ClearFilterOf', label())" (click)="clear($event)">
            <i icon="close" aria-hidden="true"></i>
          </button>
        }
        @if (isSortable()) {
          <ui-grid-header-sort [column]="column()" />
        }
      </div>
    }
  `,
  host: { class: GRID_HEADER_HOST },
})
export class GridHeaderReferenceComponent extends GridCellBase<GridHeaderReferenceModel> {
  protected readonly texts = inject(TextsService);
  protected readonly select = inject(SelectService);
  private readonly injector = inject(Injector);
  private readonly anchor = viewChild.required<ElementRef<HTMLElement>>('anchor');

  protected readonly label = computed(() => columnLabelOf(this.column()));
  protected readonly isSortable = computed(() => this.column().isSortable && !!this.grid);
  /** What the select picks from: the column's filter service or its options; null when it has neither. */
  protected readonly source = computed(() => selectSourceOf(this.column()));

  protected readonly canPick = computed(() => this.source() !== null && !!this.grid);
  protected readonly sort = computed(() => this.grid?.sortOf(this.column().path) ?? null);

  /** What the column's filter picked, read from the grid's filters so a cell's link and a chip's removal show here. */
  protected readonly picked = computed(() => this.grid?.referencePicksOf(this.column().filterPath) ?? []);

  protected readonly count = computed(() => this.picked().length);
  protected readonly showsControls = computed(() => this.sort() !== null || this.count() > 0);
  protected readonly labelClasses = computed(() =>
    this.canPick() ? 'text-link hover:text-link-hover cursor-pointer' : 'text-ink cursor-default',
  );

  /** The button's name: what it picks, and how many are picked while any are. */
  protected readonly name = computed(() => {
    const pick = this.texts.of('Pick', this.label());
    return this.count() > 0 ? [pick, this.texts.of('PickedCount', this.count())].join(', ') : pick;
  });

  constructor() {
    super();
    const subscription = this.select.committed.subscribe(options => {
      const field = this.column().filterPath;
      if (this.grid && field) {
        this.grid.setReferenceFilter(field, options);
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  protected open(event: MouseEvent | KeyboardEvent): void {
    event.stopPropagation();
    const model = this.source();
    if (!this.canPick() || !model) {
      return;
    }
    const picked = this.picked();
    this.select.open(
      { model, picked, multiple: event.altKey || event.metaKey || picked.length > 1 },
      this.anchor().nativeElement,
      this.injector,
    );
  }

  /**
   * `Enter` opens the select itself rather than through the button's click,
   * so `Alt` or `Meta` held with it makes a multi-pick as at a click.
   */
  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.open(event);
    }
  }

  protected clear(event: MouseEvent): void {
    event.stopPropagation();
    const field = this.column().filterPath;
    if (this.grid && field) {
      this.grid.setReferenceFilter(field, []);
    }
  }
}
