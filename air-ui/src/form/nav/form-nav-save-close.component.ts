import { Component, computed, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { ButtonVariant, FormNavSaveCloseModel } from '../../core/generated/contracts';
import { injectForm } from '../inject-form';

/**
 * The nav's `Save & Close`: posts the record and closes the overlay with it; `Save` alone
 * where there is no overlay to close. It acts on the form of the layout it stands in,
 * published by the `form` panel — or on a form provided around it. Without one it draws nothing.
 */
@Component({
  selector: 'ui-form-nav-save-close',
  imports: [BtnComponent],
  template: `
    @if (form(); as form) {
      <ui-btn
        [model]="model()"
        [label]="label()"
        [variant]="variant()"
        [disabled]="!form.canSave()"
        [loading]="form.isSaving() || form.isRecovering()"
        (click)="form.saveAndClose()"
      />
    }
  `,
})
export class FormNavSaveCloseComponent {
  readonly model = model<FormNavSaveCloseModel | null>();

  protected readonly form = injectForm();

  protected readonly variant = computed<ButtonVariant>(() => (this.form()?.isDirty() ? 'solid' : 'soft'));

  protected readonly label = computed<string | null>(() => {
    const label = this.model()?.label ?? null;
    const form = this.form();
    return form?.canClose() ? label : (form?.model()?.dirtyDialog.saveLabel ?? label);
  });
}
