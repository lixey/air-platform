import { Component, computed, inject } from '@angular/core';
import type { GridCellReferenceModel } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { optionOfReference } from '../../select/select-option';
import { GridCellBase } from './grid-cell-base';
import { referenceOf } from './grid-cell-format';

/**
 * A reference's name as a link: the value is a `Reference`
 * (`id`, `name`) the server projected from the navigation, and a click filters the grid by that
 * very row — the one general filter of the column's key, listed as a chip. Empty where the
 * navigation is null; plain text outside a grid or on a pinned row.
 */
@Component({
  selector: 'ui-grid-cell-reference',
  template: `
    @if (canFilter()) {
      <button
        type="button"
        class="block max-w-full truncate text-left text-link hover:text-link-hover hover:underline hover:underline-offset-4 cursor-pointer"
        [title]="texts.of('FilterBy', text())"
        (click)="filter($event)"
      >
        {{ text() }}
      </button>
    } @else {
      <span class="block truncate" [title]="text()">{{ text() }}</span>
    }
  `,
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellReferenceComponent extends GridCellBase<GridCellReferenceModel> {
  protected readonly texts = inject(TextsService);
  protected readonly reference = computed(() => referenceOf(this.value()));

  protected readonly text = computed(() => this.reference()?.name ?? '');

  protected readonly canFilter = computed(
    () => !!this.grid && !this.isPinned() && !this.isDisabled() && !!this.column().filterPath && this.reference() !== null,
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
