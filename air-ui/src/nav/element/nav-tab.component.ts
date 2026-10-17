import { Component, computed, inject, model, signal, type WritableSignal } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { NavTabModel } from '../../core/generated/contracts';
import { PanelsContext, panelValue } from '../../panels/panels-context';
import { PANELS } from '../../panels/panels-tokens';

/**
 * A tab writes its value into the shared value named by its group, and reads as selected
 * while that value is its own. The panels beside the nav switch what they show by
 * reading the same value, and never by finding the nav. A group nobody wrote reads
 * empty, and empty means the tab the server marked selected: that tab reads selected until
 * the user chooses, and nothing writes the group before.
 *
 * A tab whose model names a `path` also opens that panel while it is a page — a form's tab
 * on a phone, where the form is one page among its child tables — and reads selected only
 * while the panel is open. The content still reads the group alone.
 *
 * It is a tab of the tablist the nav draws around the tabs of its group.
 */
@Component({
  selector: 'ui-nav-tab',
  imports: [BtnComponent],
  template: '<ui-btn role="tab" [model]="model()" [selected]="isSelected()" (click)="select()" />',
})
export class NavTabComponent {
  private readonly context = inject(PanelsContext, { optional: true });
  private readonly panels = inject(PANELS, { optional: true });

  readonly model = model<NavTabModel | null>();

  /** The group's shared value; a tab with no context around it keeps one of its own. */
  private readonly group = computed<WritableSignal<string>>(() => {
    const name = this.model()?.group ?? '';
    return this.context?.of(panelValue(name, '')) ?? signal('');
  });

  /** Selected while the group holds its value, or, untouched and empty, when the server named it the initial tab. */
  protected readonly isSelected = computed(() => {
    const model = this.model();
    const value = model?.value;
    const current = this.group()();
    const isOpen = !model?.path || !this.panels || !this.panels.isCollapsed(model.path);
    return !!value && (current === value || (current === '' && !!model.isSelected)) && isOpen;
  });

  protected select(): void {
    const model = this.model();

    if (!model?.value) {
      return;
    }

    this.group().set(model.value);

    if (model.path && this.panels?.pageOf(model.path)) {
      this.panels.open(model.path);
    }
  }
}
