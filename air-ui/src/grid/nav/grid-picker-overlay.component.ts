import { Component, model } from '@angular/core';
import type { GridPickerOverlayModel } from '../../ui-components';
import { GridComponent } from '../grid.component';

/**
 * The body of a picker's list, `grid.picker`: the source's grid over the model and the page its opener read, and
 * nothing else. The grid finds the picker's select by injection, so it is a list that picks: its own nav above the
 * table, no pager, no selection, its first row active, a row click the pick. The grid puts no cap of its own on its
 * table: the card bounds the list, at the room under its top edge where it hangs under its field and at the card's own
 * height in a window of its own and in a narrow window, so the nav stands above the table and the whole shrinks with
 * the card.
 */
@Component({
  selector: 'ui-grid-picker-overlay',
  imports: [GridComponent],
  template: `
    @if (model(); as model) {
      <div class="flex flex-col flex-1 min-w-0 min-h-0">
        <ui-grid [model]="model.grid" [data]="model.page" />
      </div>
    }
  `,
  host: { class: 'flex flex-col min-w-0 max-h-full overflow-hidden' },
})
export class GridPickerOverlayComponent {
  readonly model = model<GridPickerOverlayModel | null>();
}
