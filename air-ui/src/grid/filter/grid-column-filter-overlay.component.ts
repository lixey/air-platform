import { Component, computed, inject, input, untracked, viewChild } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { GridColumnDto, OverlayDto } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { NavComponent } from '../../nav/nav.component';
import { OverlayRef } from '../../overlay/overlay-ref';
import { singlePanelLayout } from '../../panels/single-panel-layout';
import type { GridColumnFilterOverlayModel } from '../../ui-components';
import { columnLabelOf } from '../column/grid-column-label';
import { GridService } from '../grid.service';
import { FilterTreeComponent } from './filter-tree.component';

/** The key of the column filter's layout, and the stem of its overlay's key. */
const GRID_FILTER_OVERLAY = 'grid.columnFilter';

/** A menu hanging from the header's button, whose one panel is the column's filter overlay. */
export function gridColumnFilterOverlay(column: GridColumnDto): OverlayDto {
  return {
    key: `${GRID_FILTER_OVERLAY}.${column.path ?? ''}`,
    presentation: 'menu',
    layout: singlePanelLayout(GRID_FILTER_OVERLAY, { kind: 'GridColumnFilterOverlay', column }),
  };
}

/**
 * One column's filter, opened as a menu from its header: the bar — `Apply`, the column's label, the close control —
 * over the filter tree of that column alone (`ui-filter-tree`), each condition an operator from the list the server
 * sent for the column and a value typed in the library's control for the column's type (none for an operator that takes
 * none), joined in groups by AND or OR. Applying writes one column filter into the grid; clearing removes it.
 */
@Component({
  selector: 'ui-grid-column-filter-overlay',
  imports: [BtnComponent, FilterTreeComponent, NavComponent],
  template: `
    <div class="flex flex-col w-160 max-w-full min-w-0 overflow-x-hidden">
      <ui-nav class="shrink-0 border-b border-line bg-surface">
        <ui-btn start [label]="texts.of('Apply')" icon="apply" color="emerald" (click)="apply()" />
        <h2 class="m-0 min-w-0 truncate text-base font-semibold">{{ label() }}</h2>
        <ui-btn end icon="close" variant="ghost" color="slate" [title]="texts.of('Close')" (click)="ref?.dismiss()" />
      </ui-nav>
      <ui-filter-tree class="p-3" fixed seeded [filter]="opening()" [fields]="fields()" (applied)="apply()" (cleared)="clear()" />
    </div>
  `,
  host: { class: 'block' },
})
export class GridColumnFilterOverlayComponent {
  protected readonly texts = inject(TextsService);
  private readonly grid = inject(GridService, { optional: true });
  protected readonly ref = inject(OverlayRef, { optional: true });

  readonly model = input<GridColumnFilterOverlayModel | null>();

  protected readonly column = computed(() => this.model()?.column ?? null);
  protected readonly label = computed(() => {
    const column = this.column();
    return column ? columnLabelOf(column) : '';
  });
  /** The one column the tree's conditions filter. */
  protected readonly fields = computed<readonly GridColumnDto[]>(() => {
    const column = this.column();
    return column ? [column] : [];
  });

  /** The column's filter as the menu opens on it: read again only for another model, never after the grid's filters change. */
  protected readonly opening = computed(() => {
    const path = this.column()?.path;
    return untracked(() => this.grid?.columnFilterOf(path));
  });

  private readonly tree = viewChild.required(FilterTreeComponent);

  protected apply(): void {
    const path = this.column()?.path;
    if (!path || !this.grid) {
      return;
    }
    const node = this.tree().node();
    if (node) {
      this.grid.setColumnFilter(node);
    } else {
      this.grid.clearColumnFilter(path);
    }
    this.ref?.dismiss();
  }

  protected clear(): void {
    const path = this.column()?.path;
    if (path && this.grid) {
      this.grid.clearColumnFilter(path);
    }
    this.ref?.dismiss();
  }
}
