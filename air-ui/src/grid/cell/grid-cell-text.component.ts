import { Component, computed } from '@angular/core';
import type { GridCellTextModel } from '../../core/generated/contracts';
import { GridCellBase } from './grid-cell-base';

/**
 * The value as text: on one line, unless the column wraps, in which case the text wraps and the row takes the height
 * it needs. What a text column draws, and what the table draws for a cell whose kind nobody registered.
 */
@Component({
  selector: 'ui-grid-cell-text',
  template:
    '<span [class]="wraps() ? \'block whitespace-normal wrap-break-word py-2\' : \'block truncate\'" [title]="wraps() ? null : text()">{{ text() }}</span>',
  host: {
    '[class]': 'wraps() ? "flex items-start h-full min-w-0 px-2" : "flex items-center h-full min-w-0 px-2"',
  },
})
export class GridCellTextComponent extends GridCellBase<GridCellTextModel> {
  protected readonly text = computed(() => {
    const value = this.cell().value;
    return value === null || value === undefined ? '' : String(value);
  });

  protected readonly wraps = computed(() => this.cell().column.isWrapped === true);
}
