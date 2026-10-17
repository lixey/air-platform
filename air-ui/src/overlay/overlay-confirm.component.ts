import { Component, computed, inject, model, signal } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextBoxComponent } from '../controls/text-box.component';
import type { OverlayConfirmModel } from '../core/generated/contracts';
import { resolveModel } from '../core/resolve-model';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { OverlayRef } from './overlay-ref';

/**
 * What a confirmation says when its model leaves it out: the platform's texts in the request's culture. A typed answer
 * has no default: a confirmation without one confirms by its button alone.
 */
function confirmDefaults(
  texts: TextsService,
): Required<{ [K in keyof Omit<OverlayConfirmModel, 'typedAnswer'>]: NonNullable<OverlayConfirmModel[K]> }> {
  return {
    kind: 'OverlayConfirm',
    message: '',
    title: texts.of('Confirm'),
    confirmLabel: texts.of('Confirm'),
    cancelLabel: texts.of('Cancel'),
    color: 'primary',
  };
}

/**
 * The confirmation dialog's one panel: a question and two answers.
 *
 * A panel component like any other — the registry mounts it from a server element or from
 * `ConfirmationService` — that closes the overlay it is in through the injected ref. Its
 * two buttons are written with template inputs, which is the frontend-only half of the
 * model-and-inputs rule beside the server-driven half that put this panel here.
 *
 * A model carrying `typedAnswer` asks the user to type it: a text field, labelled by the platform's text naming the
 * answer, takes the focus when the dialog opens, and the confirming button, like `Enter` in the field, confirms only
 * once the field holds the answer, both compared after trimming.
 */
@Component({
  selector: 'ui-overlay-confirm',
  imports: [BtnComponent, TextBoxComponent, UiIconDirective],
  template: `
    <header class="flex items-center gap-(--ui-panel-gap) px-3 py-2 border-b border-line">
      <i icon="question" class="text-muted"></i>
      <h2 [id]="id + '-title'" class="m-0 text-base font-semibold">{{ view().title }}</h2>
    </header>

    <p [id]="id + '-message'" class="m-0 px-3 py-4 max-w-prose whitespace-pre-line">{{ view().message }}</p>

    @if (answer(); as answer) {
      <div class="flex flex-col gap-1 px-3 pb-4" data-confirm-typed>
        <label [for]="id + '-answer'" class="text-sm">{{ texts.of('ConfirmTyped', answer) }}</label>
        <!-- Focus starts in the field: the answer typed is what confirms. -->
        <ui-text-box [id]="id + '-answer'" autofocus [allowsNull]="false" [(value)]="typed" (keydown.enter)="confirmTyped($event)" />
      </div>
    }

    <footer class="flex justify-end gap-(--ui-panel-gap) px-3 py-2 border-t border-line">
      <!-- Without a typed answer focus starts on the safe answer: a stray Enter declines. -->
      <ui-btn [attr.autofocus]="answer() ? null : ''" [label]="view().cancelLabel" color="slate" (click)="ref?.close(false)" />
      <ui-btn
        [label]="view().confirmLabel"
        [color]="view().color"
        variant="solid"
        [disabled]="!isAnswered()"
        (click)="confirm()"
        data-confirm
      />
    </footer>
  `,
  host: {
    class: 'flex flex-col min-w-80',
    '[attr.role]': "ref ? null : 'alertdialog'",
    '[attr.aria-labelledby]': "ref ? null : id + '-title'",
    '[attr.aria-describedby]': "ref ? null : id + '-message'",
  },
})
export class OverlayConfirmComponent {
  private static next = 0;

  protected readonly ref = inject(OverlayRef<boolean>, { optional: true });
  protected readonly texts = inject(TextsService);

  /** Ids the title and the message carry, so the dialog can be named and described by them. */
  protected readonly id = `ui-confirm-${++OverlayConfirmComponent.next}`;

  readonly model = model<OverlayConfirmModel | null>();

  protected readonly view = computed(() => resolveModel(confirmDefaults(this.texts), this.model(), {}));

  /** What the user must type to confirm; none when the button alone confirms. */
  protected readonly answer = computed(() => this.model()?.typedAnswer?.trim() || null);

  /** What the user typed in the field of a typed answer. */
  protected readonly typed = signal<string | null>('');

  /** Whether the confirming button confirms: always without a typed answer, else once the field holds it. */
  protected readonly isAnswered = computed(() => {
    const answer = this.answer();
    return answer === null || (this.typed() ?? '').trim() === answer;
  });

  constructor() {
    // The card is the alert dialog, not a dialog holding one; outside an overlay the panel says it itself
    this.ref?.name({ role: 'alertdialog', labelledBy: `${this.id}-title`, describedBy: `${this.id}-message` });
  }

  protected confirm(): void {
    if (this.isAnswered()) {
      this.ref?.close(true);
    }
  }

  /** `Enter` in the field of a typed answer confirms once it holds the answer, and does nothing else before. */
  protected confirmTyped(event: Event): void {
    event.preventDefault();
    this.confirm();
  }
}
