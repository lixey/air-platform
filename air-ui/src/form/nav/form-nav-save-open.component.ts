import { Component, computed, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { FormNavSaveOpenModel } from '../../core/generated/contracts';
import { injectForm } from '../inject-form';

/**
 * The button of a child page on a new record's nav: a press is the form's own save, and after its commit the page at
 * the model's `path` opens on the saved record. It acts on the form of the layout it stands in, published by the `form`
 * panel — or on a form provided around it. Without one it draws nothing. It is disabled while the form may not save,
 * once the form holds an id and while a page a press asked for is still to open: the nav of the saved record is on
 * its way.
 */
@Component({
  selector: 'ui-form-nav-save-open',
  imports: [BtnComponent],
  template: `
    @if (form(); as form) {
      <ui-btn [model]="model()" [disabled]="isDisabled()" (click)="press()" />
    }
  `,
})
export class FormNavSaveOpenComponent {
  readonly model = model<FormNavSaveOpenModel | null>();

  protected readonly form = injectForm();

  protected readonly isDisabled = computed(() => {
    const form = this.form();
    return !form?.canSave() || form.id() !== null || form.pageToOpen() !== null;
  });

  /** A click reaches the element although its button is inert, so the rule of the button is asked again. */
  protected press(): void {
    const page = this.model()?.path;
    if (page && !this.isDisabled()) {
      void this.form()?.saveAndOpen(page);
    }
  }
}
