import { Component, inject, model } from '@angular/core';
import { SearchBoxComponent } from '../../controls/search-box.component';
import type { GridNavSearchModel } from '../../core/generated/contracts';
import { GridService } from '../grid.service';

/**
 * The grid's own search: it writes what was typed into the grid's search, shows the search the
 * grid opened with, and takes the caret when the grid's model asks for it. In the list of a picker it is the remote of
 * the field the list was opened from (`GridService.pickerField`): its box holds the field's own text, takes the caret
 * when drawn, and hands what it says after its pause and every key to the field's handlers, so the rule of the least
 * length and the keys stay the field's; it names the list's table and the active cell those keys move. Outside a grid
 * there is nothing to search, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-search',
  imports: [SearchBoxComponent],
  template: `
    @if (grid; as grid) {
      @if (grid.pickerField(); as field) {
        <ui-search-box
          class="w-40"
          [(text)]="field.text"
          [placeholder]="model()?.label"
          [controls]="grid.tableIds()?.table"
          [activeDescendant]="grid.tableIds()?.activeCell()"
          [autofocus]="true"
          (searched)="field.searched($event)"
          (keydown)="field.keydown($event)"
        />
      } @else {
        <ui-search-box
          class="w-40"
          [text]="grid.search()"
          [placeholder]="model()?.label"
          [autofocus]="autofocus()"
          (searched)="write($event)"
        />
      }
    }
  `,
})
export class GridNavSearchComponent {
  protected readonly grid = inject(GridService, { optional: true });

  readonly model = model<GridNavSearchModel | null>();

  protected autofocus(): boolean {
    return this.grid?.model()?.focusesSearch === true;
  }

  protected write(text: string): void {
    this.grid?.setSearch(text);
  }
}
