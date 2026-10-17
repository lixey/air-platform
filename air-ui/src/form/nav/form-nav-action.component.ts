import { Component, computed, inject, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { FormNavActionModel } from '../../core/generated/contracts';
import { unavailableReasonOf } from '../../nav/nav-availability';
import { NAV_MENU } from '../../nav/nav-menu';
import type { FormService } from '../form.service';
import { injectForm } from '../inject-form';

/**
 * An action of the nav: runs a method of the form service over the open record. The form of the layout it stands in
 * asks the confirmation, saves a dirty or new record first, posts and reads the screen again; the button only starts
 * it, closing the menu it stands in. While the record the form holds does not offer the action, the button is
 * unavailable and says the reason the record names, and starts nothing. Without a form there is no record to act on, and it draws nothing.
 */
@Component({
  selector: 'ui-form-nav-action',
  imports: [BtnComponent],
  template: `
    @if (form(); as form) {
      <ui-btn [model]="model()" [disabled]="isDisabled()" [reason]="reason()" [loading]="isLoading()" (click)="act(form)" />
    }
  `,
})
export class FormNavActionComponent {
  readonly model = model<FormNavActionModel | null>();

  protected readonly form = injectForm();
  private readonly menu = inject(NAV_MENU, { optional: true });

  /** Disabled while the form may not act, and where the server delivered the button disabled. */
  protected readonly isDisabled = computed(() => !this.form()?.canAct() || !!this.model()?.isDisabled);

  /** Why the record the form holds does not offer the action; null while it does. */
  protected readonly reason = computed(() => unavailableReasonOf(this.model(), [this.form()?.unavailable()]));

  /** The pressed button alone shows the busy mark: the form names the action under way by its method. */
  protected readonly isLoading = computed(() => {
    const method = this.model()?.method;
    return !!method && this.form()?.actingMethod() === method;
  });

  /** A click reaches the element although its button is inert, so the rule of the button is asked again. */
  protected act(form: FormService): void {
    const action = this.model();
    if (action && !this.isDisabled() && !this.reason()) {
      this.menu?.close();
      void form.act(action);
    }
  }
}
