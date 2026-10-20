import { Component, computed, inject, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { GridNavActionModel } from '../../core/generated/contracts';
import { isRecord } from '../../core/json';
import { unavailableReasonOf, type Unavailable } from '../../nav/nav-availability';
import { NAV_MENU } from '../../nav/nav-menu';
import type { GridRow } from '../grid-row';
import { GridService } from '../grid.service';
import { GRID_DELETE, GridActionsService } from './grid-actions.service';

/** What a row says it does not offer, its `unavailable`: the keys with their indexes; null for a row that says none. */
function unavailableOf(row: GridRow): Unavailable | null {
  const value = row['unavailable'];
  return isRecord(value)
    ? Object.fromEntries(Object.entries(value).filter((entry): entry is [string, number] => typeof entry[1] === 'number'))
    : null;
}

/**
 * A button that starts an action of the surrounding grid: the grid's `GridActionsService` runs it, in the grid's scope,
 * so the answer applies whether this button still stands or not, and the button reads busy while its action is under
 * way wherever it is drawn. It closes the menu it stands in as the action starts. Disabled while the model needs a
 * selection and there is none, and where the server delivered the button disabled; unavailable, saying the reason,
 * while a selected row does not offer the action; outside a grid there is nothing to post, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-action',
  imports: [BtnComponent],
  template: `
    @if (isVisible()) {
      <ui-btn [model]="model()" [disabled]="isDisabled()" [reason]="reason()" [loading]="isBusy()" (click)="run()" />
    }
  `,
  host: { class: 'contents' },
})
export class GridNavActionComponent {
  protected readonly grid = inject(GridService, { optional: true });
  private readonly actions = inject(GridActionsService, { optional: true });
  private readonly menu = inject(NAV_MENU, { optional: true });

  readonly model = model<GridNavActionModel | null>();

  protected readonly isVisible = computed(() => {
    const action = this.model();
    return !!action && !!this.grid && (!action.isSingle || this.grid.selection.count() === 1);
  });

  protected readonly isBusy = computed(() => {
    const action = this.model();
    return !!action && !!this.actions?.isRunning(action);
  });

  protected readonly isDisabled = computed(() => {
    const action = this.model();
    return (
      !action ||
      !this.grid ||
      !!action.isDisabled ||
      (action.needsSelection && this.grid.selection.isEmpty()) ||
      (action.isSingle && this.grid.selection.count() !== 1) ||
      (action.method === GRID_DELETE && this.grid.deletion.isBlocked())
    );
  });

  /** Why the selected rows do not offer the action: the reason of the first row that names it; null while every one offers it. */
  protected readonly reason = computed(() => unavailableReasonOf(this.model(), this.grid?.selection.selected().map(unavailableOf) ?? []));

  /** A click reaches the element although its button is inert, so the rules of the button are asked again. */
  protected run(): void {
    const action = this.model();
    if (!action || !this.actions || this.isDisabled() || this.reason() || this.isBusy()) {
      return;
    }
    this.menu?.close();
    void this.actions.run(action);
  }
}
