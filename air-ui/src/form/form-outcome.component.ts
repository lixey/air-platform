import { Component, inject } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { FormService } from './form.service';

/**
 * Recovery belongs to this form; committed warnings belong to the application alerts. While no save is unsettled, a
 * stale form shows here that its record changed, with the button that reads it again.
 */
@Component({
  selector: 'ui-form-outcome',
  imports: [BtnComponent],
  template: `
    @if (form.model(); as model) {
      @if (form.outcome() === 'conflict' || form.outcome() === 'unknown') {
        <div class="m-3 p-3 rounded-lg border border-line bg-surface-2" role="alert">
          <p class="m-0 font-medium">
            {{ form.outcome() === 'conflict' ? model.mutationTexts.conflictTitle : model.mutationTexts.unknownTitle }}
          </p>
          @if (form.outcome() === 'unknown') {
            <p class="mt-1 text-sm">{{ model.mutationTexts.unknownDescription }}</p>
          }
          @if (form.canRecover()) {
            <ui-btn
              class="mt-2"
              [label]="form.outcome() === 'conflict' ? model.mutationTexts.reloadLabel : model.mutationTexts.verifyLabel"
              [disabled]="form.isBusy()"
              [loading]="form.isRecovering()"
              (click)="form.recover()"
            />
          }
        </div>
      } @else if (form.isStale()) {
        <div class="m-3 p-3 rounded-lg border border-line bg-surface-2" role="status">
          <p class="m-0 font-medium">{{ texts.of('FormRecordChanged') }}</p>
          <ui-btn
            class="mt-2"
            [label]="model.mutationTexts.reloadLabel"
            [disabled]="form.isBusy()"
            [loading]="form.isRecovering()"
            (click)="form.reload()"
          />
        </div>
      }
    }
  `,
  host: { class: 'block shrink-0' },
})
export class FormOutcomeComponent {
  protected readonly form = inject(FormService);
  protected readonly texts = inject(TextsService);
}
