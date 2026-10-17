import { Component, computed, inject, model } from '@angular/core';
import type { GridNavStatusModel, NavMenuModel } from '../../core/generated/contracts';
import { isRecord, valueAt } from '../../core/json';
import { NavMenuComponent } from '../../nav/element/nav-menu.component';
import { GridService } from '../grid.service';

/** The status a row holds at the button's path: the key of a select column's reference, or the key itself. */
function statusOf(value: unknown): unknown {
  return isRecord(value) ? value['id'] : value;
}

/**
 * The Status button of the surrounding grid: a menu button, drawn as `NavMenu` draws one, whose menu holds the actions
 * of the moves every selected row's status allows, in the order given. It reads the rows the grid holds, so the offer
 * follows the selection with no request; the server checks the move again. While the selected rows allow no move the
 * button is disabled, titled with the model's `emptyTitle`, and opens nothing. Each item is an action of the grid,
 * drawn and posted by the grid's own component; outside a grid there is nothing to post, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-status',
  imports: [NavMenuComponent],
  template: `
    <ui-nav-menu [model]="menu()" />
  `,
})
export class GridNavStatusComponent {
  private readonly grid = inject(GridService, { optional: true });

  readonly model = model<GridNavStatusModel | null>();

  /** The menu of the actions of the moves whose `from` holds the status of every selected row; disabled with the hint for none. */
  protected readonly menu = computed<NavMenuModel | null>(() => {
    const model = this.model();
    if (!model || !this.grid) {
      return null;
    }

    const { kind: _kind, path, emptyTitle, moves, ...look } = model;
    const statuses = this.grid.selection.selected().map(row => statusOf(valueAt(row, path)));
    const elements = moves.filter(move => statuses.every(status => move.from.some(from => from === status))).map(move => move.action);
    return elements.length > 0
      ? { ...look, kind: 'NavMenu', elements }
      : { ...look, kind: 'NavMenu', elements: [], isDisabled: true, title: emptyTitle };
  });
}
