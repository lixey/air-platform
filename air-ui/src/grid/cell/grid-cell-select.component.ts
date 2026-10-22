import { Component, computed, inject } from '@angular/core';
import { BadgeComponent } from '../../badge/badge.component';
import type { GridCellSelectModel } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { optionOfReference } from '../../select/select-option';
import { FOCUS_RING } from '../../theme/css-classes';
import { optionOf } from '../column/grid-column-select-source';
import { GridCellBase } from './grid-cell-base';
import { referenceOf } from './grid-cell-format';

/**
 * The cell of a select column: the value is the reference (`id`, `name`) of the option the row's key names, and the
 * colour is the column's — the option of that id among the column's `options`. An option with a colour is drawn as the
 * library's badge in that hue, one without, and a key no option names, as plain text; the cell itself is never painted.
 * A click on the name filters the grid by that option — the one general filter of the column's key, listed as a chip,
 * as a reference cell's does. Empty for no key; no click outside a grid, on a pinned row or in a disabled column.
 */
@Component({
  selector: 'ui-grid-cell-select',
  imports: [BadgeComponent],
  template: `
    @if (canFilter()) {
      <button type="button" [class]="buttonClasses()" [title]="optionTitle() || texts.of('FilterBy', text())" (click)="filter($event)">
        @if (color(); as hue) {
          <ui-badge [label]="text()" [color]="hue" [title]="optionTitle()" />
        } @else {
          {{ text() }}
        }
      </button>
    } @else if (color(); as hue) {
      <ui-badge [label]="text()" [color]="hue" [title]="optionTitle()" />
    } @else {
      <span class="block truncate" [title]="optionTitle() || text()">{{ text() }}</span>
    }
  `,
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellSelectComponent extends GridCellBase<GridCellSelectModel> {
  protected readonly texts = inject(TextsService);
  protected readonly reference = computed(() => referenceOf(this.value()));

  protected readonly text = computed(() => this.reference()?.name ?? '');

  /** The hue of the option the row's key names; null for an option without one and for a key no option names. */
  protected readonly color = computed(() => {
    const reference = this.reference();
    return reference ? (optionOf(this.column(), reference.id)?.color ?? null) : null;
  });

  protected readonly optionTitle = computed(() => {
    const reference = this.reference();
    return reference ? (optionOf(this.column(), reference.id)?.title ?? null) : null;
  });

  protected readonly canFilter = computed(
    () => !!this.grid && !this.isPinned() && !this.isDisabled() && !!this.column().filterPath && this.reference() !== null,
  );

  /**
   * The button around the name: the badge's own box, edged in its ink under
   * the pointer, for a coloured option; the grid's link for a plain one.
   */
  protected readonly buttonClasses = computed(() =>
    this.color()
      ? `hue-${this.color()} flex max-w-full min-w-0 rounded-full cursor-pointer hover:shadow-[0_0_0_1px_var(--badge-ink)] ${FOCUS_RING}`
      : 'block max-w-full truncate text-left text-link hover:text-link-hover hover:underline hover:underline-offset-4 cursor-pointer',
  );

  protected filter(event: MouseEvent): void {
    event.stopPropagation();
    const reference = this.reference();
    const field = this.column().filterPath;
    if (this.grid && field && reference) {
      this.grid.setReferenceFilter(field, [optionOfReference(reference)]);
    }
  }
}
