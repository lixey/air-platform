import { Component, computed, inject, model, signal, type WritableSignal } from '@angular/core';
import { SearchBoxComponent } from '../../controls/search-box.component';
import type { NavSearchModel } from '../../core/generated/contracts';
import { PanelsContext, panelValue } from '../../panels/panels-context';

/**
 * The nav's search: it writes what was typed, a moment after the typing stops, into the shared
 * value its model names, and shows what that value already holds. It knows nothing of what reads
 * the value — a grid's own search is `GridNavSearch`, a component of the grid.
 */
@Component({
  selector: 'ui-nav-search',
  imports: [SearchBoxComponent],
  template: `
    <ui-search-box class="w-40" [text]="panelText()()" [placeholder]="model()?.label" (searched)="write($event)" />
  `,
})
export class NavSearchComponent {
  private readonly context = inject(PanelsContext, { optional: true });

  readonly model = model<NavSearchModel | null>();

  protected readonly panelText = computed<WritableSignal<string>>(() => {
    const name = this.model()?.value ?? '';
    return this.context?.of(panelValue(name, '')) ?? signal('');
  });

  protected write(text: string): void {
    this.panelText().set(text);
  }
}
