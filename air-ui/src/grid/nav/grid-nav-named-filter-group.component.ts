import { Component, computed, inject, model } from '@angular/core';
import type { GridNavNamedFilterGroupModel, NavMenuModel } from '../../core/generated/contracts';
import { NavMenuComponent } from '../../nav/element/nav-menu.component';
import { GridService } from '../grid.service';

/**
 * A group of named filters of the surrounding grid, one of which is chosen: a menu button, drawn as `NavMenu` draws
 * one, that reads the label of the chosen item in that item's colour and is titled by the group's label when it has
 * one. Its menu holds the items, each drawn and pressed by the named filter's own component, the chosen one pressed.
 * The chosen item is the one the grid holds, so the button follows a press with no new model. Outside a grid there is
 * nothing to choose, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-named-filter-group',
  imports: [NavMenuComponent],
  template: `
    <ui-nav-menu [model]="menu()" />
  `,
})
export class GridNavNamedFilterGroupComponent {
  private readonly grid = inject(GridService, { optional: true });

  readonly model = model<GridNavNamedFilterGroupModel | null>();

  /** The menu of the items under the chosen item's label and colour; the group's own label while none is chosen. */
  protected readonly menu = computed<NavMenuModel | null>(() => {
    const model = this.model();
    const grid = this.grid;
    if (!model || !grid) {
      return null;
    }

    const chosen = model.items.find(item => grid.isNamedFilterOn(item.key));
    return {
      kind: 'NavMenu',
      key: model.key,
      label: chosen?.label ?? model.label,
      color: chosen?.color,
      title: model.label,
      elements: model.items,
    };
  });
}
