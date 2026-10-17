import { computed, Directive, inject, input } from '@angular/core';
import type { GridColumnDto } from '../../core/generated/contracts';
import type { GridRow } from '../grid-row';
import { GridService } from '../grid.service';
import type { GridCellContext } from './grid-cell-context';

/**
 * What a cell or a header starts from: the `cell` input every one is given, the `model` of its kind, the grid's
 * service when it stands in a grid, and the row, the column and the value read from the cell. `M` is the model of the
 * kind it draws and has no default, so the registry checks the class against its kind.
 *
 * <code>
 * @Component({ selector: 'rating-cell', template: '…' })
 * export class RatingCellComponent extends GridCellBase<RatingCellModel> {}
 * </code>
 */
@Directive()
export abstract class GridCellBase<M> {
  /** The grid's service; null where the cell is drawn outside a grid. */
  protected readonly grid = inject(GridService, { optional: true });

  readonly cell = input.required<GridCellContext>();

  /** The model of the kind the cell is drawn by; absent where the table draws a fallback. */
  readonly model = input<M>();

  protected readonly row = computed<GridRow | null>(() => this.cell().row);
  protected readonly column = computed<GridColumnDto>(() => this.cell().column);
  protected readonly value = computed<unknown>(() => this.cell().value);

  /** Whether the column is disabled: shown and acting on nothing. */
  protected readonly isDisabled = computed(() => this.cell().column.isDisabled === true);

  /** Whether the row is one the page pins above or below itself. */
  protected readonly isPinned = computed(() => this.cell().pinned !== undefined);
}
