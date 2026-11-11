import { Component, computed, inject, LOCALE_ID, model } from '@angular/core';
import type { GridNavNamedFilterGroupModel, NavMenuModel } from '../../core/generated/contracts';
import { NavMenuComponent } from '../../nav/element/nav-menu.component';
import { formatCount } from '../cell/grid-cell-format';
import { GridService } from '../grid.service';
import { GridNavNamedFilterComponent } from './grid-nav-named-filter.component';

/**
 * A group of named filters of the surrounding grid, one of which is chosen: either an inline strip of filter buttons
 * (`isInline`) or a menu button (`NavMenu`) reading the label, colour and live count of the chosen item.
 */
@Component({
  selector: 'ui-grid-nav-named-filter-group',
  imports: [NavMenuComponent, GridNavNamedFilterComponent],
  template: `
    @if (model()?.isInline) {
      @if (grid && model(); as m) {
        <div class="inline-flex items-center gap-1 shrink-0" role="group" [attr.aria-label]="m.label || null">
          @for (item of m.items; track item.key) {
            <ui-grid-nav-named-filter [model]="item" />
          }
        </div>
      }
    } @else {
      <ui-nav-menu [model]="menu()" />
    }
  `,
})
export class GridNavNamedFilterGroupComponent {
  protected readonly grid = inject(GridService, { optional: true });
  private readonly locale = inject(LOCALE_ID);

  readonly model = model<GridNavNamedFilterGroupModel | null>();

  /** The menu of the items under the chosen item's label, colour and live count; the group's own label while none is chosen. */
  protected readonly menu = computed<NavMenuModel | null>(() => {
    const model = this.model();
    const grid = this.grid;
    if (!model || !grid) {
      return null;
    }

    const chosen = model.items.find(item => grid.isNamedFilterOn(item.key));
    const liveCount = chosen ? grid.namedFilterCountOf(chosen.key) : null;
    const badge = liveCount !== null ? formatCount(liveCount, this.locale) : (chosen?.badge ?? null);
    return {
      kind: 'NavMenu',
      key: model.key,
      label: chosen?.label ?? model.label,
      color: chosen?.color,
      badge: badge ?? undefined,
      title: model.label,
      elements: model.items,
    };
  });
}
