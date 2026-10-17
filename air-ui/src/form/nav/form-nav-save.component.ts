import { Component, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { FormNavSaveModel } from '../../core/generated/contracts';
import { injectForm } from '../inject-form';

/**
 * The nav's `Save`: posts the record and stays on it. It acts on the form of the layout it
 * stands in, published by the `form` panel — or on a form provided around it, where a form
 * is mounted by hand. Without one there is nothing to save, and it draws nothing.
 */
@Component({
  selector: 'ui-form-nav-save',
  imports: [BtnComponent],
  template: `
    @if (form(); as form) {
      @if (form.canClose()) {
        <ui-btn [model]="model()" [disabled]="!form.canSave()" [loading]="form.isSaving() || form.isRecovering()" (click)="form.save()" />
      }
    }
  `,
})
export class FormNavSaveComponent {
  readonly model = model<FormNavSaveModel | null>();

  protected readonly form = injectForm();
}
