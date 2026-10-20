import { Component, computed, inject, input } from '@angular/core';
import { DynamicOutletDirective } from '../../core/dynamic-component/dynamic-outlet.directive';
import type { CellModel, HeaderModel } from '../../core/generated/contracts';
import { valueAt } from '../../core/json';
import type { UiColor } from '../../theme/ui-color';
import { UI_COLORS } from '../../theme/ui-color';
import { SELECTION_PATH } from '../column/grid-selection-column';
import type { GridRow } from '../grid-row';
import { GridService } from '../grid.service';
import type { GridCellContext } from './grid-cell-context';

/**
 * A cell painted in its hue, over the whole cell: the hue's cell ground and its semibold ink, the grid's link inks turned
 * to that ink so a reference or an open-form cell reads in the hue and still acts as a link; while the pointer is anywhere
 * on the row, the hovered ground and ink. The ground is important, over the sticky cell's inherited ground; the hover
 * ring and the active cell's ring are drawn above it.
 */
const PAINTED =
  'font-semibold bg-(--hue-cell)! text-(--hue-cell-ink) [--grid-link:var(--hue-cell-ink)] [--grid-link-hover:var(--hue-cell-ink)] ' +
  '[tr:hover_&]:bg-(--hue-cell-hover)! [tr:hover_&]:text-(--hue-cell-hover-ink) [tr:hover_&]:[--grid-link:var(--hue-cell-hover-ink)] [tr:hover_&]:[--grid-link-hover:var(--hue-cell-hover-ink)]';

/** The bottom line of a painted body cell that has another body row below it. */
const PAINTED_LINE = 'shadow-[inset_0_-1px_0_var(--panel-line)]';

/**
 * One cell of the table: mounts the component the model's kind names, given the cell and the model. Put on the `td`
 * or `th` itself, so the cell component is the cell's content and the table keeps the box.
 *
 * Fallback policy: the table hands in the fallback kind of what it is drawing, and a cell without a model, or one
 * whose kind nobody registered, is drawn by it.
 *
 * Hue: when the model names a `colorPath` and the row's value there is one of `UI_COLORS`, the cell itself is painted
 * carrying `hue-<value>`; failing that, a checkbox cell outside the selection column is painted `green` for true and
 * `red` for false. Any other value, null included, draws the cell as it is. A select cell is never painted: the colour
 * of its option is the badge it draws. The component inside is the same, so the
 * painted cell keeps the cell's behaviour.
 */
@Component({
  selector: '[ui-grid-cell-outlet]',
  imports: [DynamicOutletDirective],
  template: `
    <ng-container [uiDynamicOutlet]="model()?.kind" [uiDynamicOutletFallback]="fallback()" [uiDynamicOutletInputs]="inputs()" />
  `,
  host: { '[class]': 'paint()' },
})
export class GridCellOutletComponent {
  private readonly grid = inject(GridService, { optional: true });

  readonly cell = input.required<GridCellContext>();
  readonly model = input<CellModel | HeaderModel | null>();
  readonly fallback = input.required<CellModel['kind'] | HeaderModel['kind']>();

  protected readonly inputs = computed(() => ({ cell: this.cell(), model: this.model() ?? undefined }));

  /** The hue the cell is painted in: the row's at the model's hue path, else a flag's; null for none. */
  protected readonly hue = computed(() => hueOf(this.cell().row, this.model()) ?? flagHueOf(this.cell(), this.model()));

  /** Whether the cell needs its own bottom line: every body row except the last, which shares the table's bottom border. */
  private readonly hasBottomLine = computed(() => {
    const cell = this.cell();
    if (cell.pinned !== undefined) {
      return false;
    }
    const rows = this.grid?.rows();
    return rows === undefined || cell.index < rows.length - 1;
  });

  /** The classes the hue adds to the cell's own; none while the cell is not painted. */
  protected readonly paint = computed(() => {
    const hue = this.hue();
    if (!hue) {
      return '';
    }
    return this.hasBottomLine() ? `hue-${hue} ${PAINTED} ${PAINTED_LINE}` : `hue-${hue} ${PAINTED}`;
  });
}

/**
 * The row's value at the model's `colorPath` when it is one of `UI_COLORS`; null for no path, no row or any other value,
 * and for a select cell, whose colour is its badge's.
 */
function hueOf(row: GridRow | null, model: CellModel | HeaderModel | null | undefined): UiColor | null {
  const path = model && model.kind !== 'GridCellSelect' && 'colorPath' in model ? model.colorPath : null;
  if (typeof path !== 'string') {
    return null;
  }
  const value = valueAt(row, path);
  return UI_COLORS.find(color => color === value) ?? null;
}

/** The hue of a checkbox cell of a data column: `green` for true, `red` for false; null for any other value or cell. */
function flagHueOf(cell: GridCellContext, model: CellModel | HeaderModel | null | undefined): UiColor | null {
  if (model?.kind !== 'GridCellCheckbox' || cell.row === null || cell.column.path === SELECTION_PATH) {
    return null;
  }
  if (cell.value === true) {
    return 'green';
  }
  return cell.value === false ? 'red' : null;
}
