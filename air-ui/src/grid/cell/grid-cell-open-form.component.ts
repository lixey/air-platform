import { Component, computed, ElementRef, inject, type Signal } from '@angular/core';
import type { GridCellOpenFormModel } from '../../core/generated/contracts';
import { valueAt } from '../../core/json';
import { TextsService } from '../../core/texts.service';
import { keyOf } from '../grid-row';
import { GridCellBase } from './grid-cell-base';
import { formatCellText } from './grid-cell-format';

/**
 * The value as a link that opens the grid's form for the row, in a dialog by address:
 * link-coloured text, the whole cell turns to the `cell-hover` ground with its white text under
 * the pointer, no underline, and the whole cell acts; a number — an id — right aligned in tabular figures, so
 * the digits line up. A row without a key, a totals row for one, shows nothing and opens nothing. Opening makes the
 * row the active one, as a click on the row does, so the grid shows where the form came from once it closes.
 *
 * It is a link only where the grid has something to act on for the row: the row names a form, the grid's model
 * declares one, or the grid is the list of a select, where the link picks the row. Where none holds — a row whose
 * form the server left out, on a grid with no form of its own — the value is plain text: no link role, not focusable,
 * no pointer and no hover ground; a click on it still makes the row the active one, and opens nothing.
 */
@Component({
  selector: 'ui-grid-cell-open-form',
  template: '<span class="block max-w-full truncate tabular-nums">{{ text() }}</span>',
  host: {
    '[class]': 'classes()',
    '[attr.role]': 'hasTarget() ? "link" : null',
    '[attr.tabindex]': 'hasTarget() ? "-1" : null',
    '[attr.aria-label]': "hasTarget() ? texts.of('OpenRecord', text()) : null",
    '(click)': 'open($event)',
  },
})
export class GridCellOpenFormComponent extends GridCellBase<GridCellOpenFormModel> {
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  protected readonly texts = inject(TextsService);

  private readonly targetKey = computed<number | null>(() => {
    const row = this.cell().row;
    const keyPath = this.model()?.keyPath;
    if (keyPath && row && typeof row === 'object') {
      const val = valueAt(row as Record<string, unknown>, keyPath);
      if (typeof val === 'number' && val !== 0) {
        return val;
      }
      if (val && typeof val === 'object' && 'id' in val && typeof (val as { id: unknown }).id === 'number') {
        const id = (val as { id: number }).id;
        return id !== 0 ? id : null;
      }
    }
    const key = keyOf(row);
    if (this.grid?.isPicking() && key === 0 && !this.cell().pinned) {
      return 0;
    }
    return key !== null && key !== 0 ? key : null;
  });

  /** Whether the row has something to open: a key that is not zero. */
  protected readonly canOpen = computed(() => {
    if (this.cell().column.isDisabled) {
      return false;
    }
    return this.targetKey() !== null;
  });

  /** Whether the grid has something the link acts on for this row: a select to pick in, or a form the row or the model names. */
  protected readonly hasTarget: Signal<boolean> = computed(() => {
    const grid = this.grid;
    if (!grid) {
      return false;
    }
    if (grid.isPicking() || typeof this.cell().row?.['form'] === 'string') {
      return true;
    }
    return Boolean(grid.model()?.form?.screen?.service);
  });

  protected readonly text = computed(() => {
    const value = this.cell().value;
    const key = keyOf(this.cell().row);
    const hasRow = key !== null && (key !== 0 || (this.grid?.isPicking() === true && !this.cell().pinned));
    return hasRow ? formatCellText(value) : '';
  });

  protected readonly classes = computed(() => {
    const align = this.cell().column.type === 'number' ? 'justify-end text-right' : '';
    const link = this.canOpen() && this.hasTarget() ? 'text-link cursor-pointer hover:bg-cell-hover hover:text-on-cell-hover' : '';
    return `flex items-center h-full min-w-0 px-2 ${align} ${link}`;
  });

  protected open(event: MouseEvent): void {
    event.stopPropagation();
    const grid = this.grid;
    const targetId = this.targetKey();
    if (grid && this.canOpen() && targetId !== null) {
      const rowKey = keyOf(this.cell().row);
      const index = grid.rows().findIndex(row => keyOf(row) === rowKey);
      if (index >= 0) {
        grid.setActive(index);
      }
      const row = this.cell().row as Record<string, unknown> | null;
      const targetForm = row && typeof row === 'object' && typeof row['form'] === 'string' ? (row['form'] as string) : null;
      if (targetForm) {
        grid.openForm(targetId, this.elementRef.nativeElement, targetForm);
      } else {
        grid.openForm(targetId, this.elementRef.nativeElement);
      }
    }
  }
}
