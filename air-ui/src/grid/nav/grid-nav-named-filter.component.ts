import { Component, computed, inject, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { GridNavNamedFilterModel } from '../../core/generated/contracts';
import { NAV_MENU } from '../../nav/nav-menu';
import { GridService } from '../grid.service';

/**
 * A named filter of the surrounding grid that is pressed: a toggle on the nav, or an item in the menu of a group.
 * A button with the look of its model that says whether it is pressed, `aria-pressed="false"` too, as the grid holds
 * it and not as the model marked it. Its badge is the count the grid's last count answered for its key, and the
 * model's until one did. A press tells the grid, which asks the list again, and closes the menu the button stands in.
 * Outside a grid there is nothing to press, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-named-filter',
  imports: [BtnComponent],
  template: `
    @if (grid) {
      <ui-btn [model]="model()" [selected]="isOn()" [badge]="badge()" (click)="press()" />
    }
  `,
})
export class GridNavNamedFilterComponent {
  protected readonly grid = inject(GridService, { optional: true });
  private readonly menu = inject(NAV_MENU, { optional: true });

  readonly model = model<GridNavNamedFilterModel | null>();

  protected readonly isOn = computed(() => {
    const model = this.model();
    return !!model && !!this.grid?.isNamedFilterOn(model.key);
  });

  protected readonly badge = computed(() => {
    const model = this.model();
    return (model ? this.grid?.namedFilterBadgeOf(model.key) : null) ?? model?.badge ?? null;
  });

  protected press(): void {
    const model = this.model();
    if (model) {
      this.grid?.pressNamedFilter(model.key);
      this.menu?.close();
    }
  }
}
