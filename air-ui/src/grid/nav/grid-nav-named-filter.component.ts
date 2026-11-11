import { Component, computed, inject, LOCALE_ID, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { GridNavNamedFilterModel } from '../../core/generated/contracts';
import { NAV_MENU } from '../../nav/nav-menu';
import type { UiColor } from '../../theme/ui-color';
import { formatCount } from '../cell/grid-cell-format';
import { GridService } from '../grid.service';

/**
 * A named filter of the surrounding grid that is pressed: a toggle on the nav, or an item in the menu of a group.
 * A button with the look of its model that says whether it is pressed, `aria-pressed="false"` too, as the grid holds
 * it and not as the model marked it. A press tells the grid, which asks the list again, and closes the menu the button
 * stands in. Outside a grid there is nothing to press, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-named-filter',
  imports: [BtnComponent],
  template: `
    @if (grid) {
      <ui-btn [model]="model()" [selected]="isOn()" [badge]="badge()" [badgeColor]="badgeColor()" (click)="press()" />
    }
  `,
})
export class GridNavNamedFilterComponent {
  protected readonly grid = inject(GridService, { optional: true });
  private readonly menu = inject(NAV_MENU, { optional: true });
  private readonly locale = inject(LOCALE_ID);

  readonly model = model<GridNavNamedFilterModel | null>();

  protected readonly isOn = computed(() => {
    const model = this.model();
    return !!model && !!this.grid?.isNamedFilterOn(model.key);
  });

  protected readonly badge = computed<string | number | null>(() => {
    const model = this.model();
    if (!model) {
      return null;
    }
    const live = this.grid?.namedFilterCountOf(model.key) ?? null;
    if (live !== null) {
      return formatCount(live, this.locale);
    }
    return model.badge ?? null;
  });

  protected readonly badgeColor = computed<UiColor>(() => this.model()?.color ?? 'slate');

  protected press(): void {
    const model = this.model();
    if (model) {
      this.grid?.pressNamedFilter(model.key);
      this.menu?.close();
    }
  }
}
