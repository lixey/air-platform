import { Component, computed, inject } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { SearchBoxComponent } from '../controls/search-box.component';
import { TextsService } from '../core/texts.service';
import { NavComponent } from '../nav/nav.component';
import { SelectService } from './select.service';

const DEBOUNCE_MS = 400;

/**
 * The bar of the select overlay: `Apply` in multiple mode, emerald with its icon; the search box
 * with its clear control, taking the caret when the overlay opens and widening while it holds text; the label of
 * what is being picked and the single pick's name; the close control. The keys on the box drive
 * the list: the arrows, `Home`, `End`, `PageUp` and `PageDown` move the active row, `Enter`
 * picks it, `Escape` closes — and says it handled the key, so the overlay under it stays. The box names
 * the list and its active option, a static list's or a grid's table and its active cell, as a combobox's box would.
 */
@Component({
  selector: 'ui-select-bar',
  imports: [BtnComponent, NavComponent, SearchBoxComponent],
  template: `
    <ui-nav class="shrink-0 border-b border-line bg-surface">
      @if (select.isMultiple()) {
        <ui-btn start [label]="texts.of('Apply')" icon="apply" color="emerald" (click)="select.apply()" />
      }
      <ui-search-box
        start
        class="shrink-0"
        [class]="searchWidth()"
        [label]="texts.of('SearchOptions')"
        [controls]="select.controlsId()"
        [activeDescendant]="select.activeDescendantId()"
        [text]="select.searchText()"
        [debounce]="DEBOUNCE_MS"
        [autofocus]="true"
        (searched)="select.search($event)"
        (keydown)="onKeyDown($event)"
      />
      <span class="min-w-0 truncate text-sm">
        <span class="text-muted">{{ select.model().label }}</span>
        @if (!select.isMultiple() && select.draft()[0]; as picked) {
          <span class="text-muted">:</span>
          <span class="text-link">{{ picked.label }}</span>
        }
      </span>
      <ui-btn end icon="close" variant="ghost" color="slate" [title]="texts.of('Close')" (click)="select.close()" />
    </ui-nav>
  `,
  host: { class: 'flex flex-col shrink-0' },
})
export class SelectBarComponent {
  protected readonly texts = inject(TextsService);
  protected readonly select = inject(SelectService);
  protected readonly DEBOUNCE_MS = DEBOUNCE_MS;

  /** The search box widens while it holds text. */
  protected readonly searchWidth = computed(() => (this.select.searchText() ? 'w-36' : 'w-28'));

  protected onKeyDown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        this.select.moveActive(1);
        break;
      case 'ArrowUp':
        this.select.moveActive(-1);
        break;
      case 'Home':
        this.select.setActive(0);
        break;
      case 'End':
        this.select.setActive(-1);
        break;
      case 'PageDown':
        this.select.moveActive(10);
        break;
      case 'PageUp':
        this.select.moveActive(-10);
        break;
      case 'Enter':
        if (!this.select.pickActive() && this.select.options().length > 0) {
          this.select.pick(this.select.options()[0]);
        }
        break;
      case 'Escape':
        this.select.close();
        break;
      default:
        return;
    }
    event.preventDefault();
  }
}
