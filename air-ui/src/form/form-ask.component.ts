import { afterNextRender, Component, computed, DestroyRef, ElementRef, inject, Injector, model, signal } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import type { FormAskModel, FormElementDto, MessageDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { ASK_SUBMIT, ASK_VALUE } from '../overlay/ask.service';
import { OverlayRef } from '../overlay/overlay-ref';
import { FormElementOutletComponent } from './form-element-outlet.component';
import { spanClassOf } from './form-span';
import { type FormInput, FormService } from './form.service';

/**
 * The dialog that asks an action's input before the action is posted: a title, the question of the action's
 * confirmation when it has one, and the declared elements, drawn by the components of a form's elements over a form of
 * input alone, which opens with the values its opener gives (`ASK_VALUE`), a reference with its name at the caption
 * path beside its key, else empty.
 *
 * `Confirm` hands the values at the elements' paths, and no caption of a reference beside them, to the opener's
 * submission (`ASK_SUBMIT`) and waits for it, the fields inert and the close refused meanwhile.
 * Messages it answers stand under the fields their paths name, the first of them focused, and the
 * dialog stays; none closes it with `true`. `Cancel` closes it with `false` and submits nothing.
 */
@Component({
  selector: 'ui-form-ask',
  imports: [BtnComponent, FormElementOutletComponent, UiIconDirective],
  providers: [FormService],
  template: `
    <header class="flex items-center gap-(--ui-panel-gap) px-3 py-2 border-b border-line">
      <i icon="question" class="text-muted"></i>
      <h2 [id]="id + '-title'" class="m-0 text-base font-semibold">{{ title() }}</h2>
    </header>

    @if (model()?.message; as message) {
      <p class="m-0 px-3 pt-4 max-w-prose whitespace-pre-line" data-ask-message>{{ message }}</p>
    }

    <div class="p-3 @container" [attr.inert]="isSubmitting() ? '' : null" data-ask-body>
      <div class="grid grid-cols-12 gap-x-4 gap-y-3 content-start">
        @for (element of form.elements(); track element.path) {
          <div class="min-w-0" [class]="spanOf(element)">
            <ui-form-element-outlet [element]="element" />
          </div>
        }
      </div>
    </div>

    <footer class="flex justify-end gap-(--ui-panel-gap) px-3 py-2 border-t border-line">
      <ui-btn [label]="texts.of('Cancel')" color="slate" [disabled]="isSubmitting()" (click)="cancel()" data-ask-cancel />
      <ui-btn
        [label]="texts.of('Confirm')"
        [color]="model()?.color ?? 'primary'"
        variant="solid"
        [loading]="isSubmitting()"
        (click)="submit()"
        data-ask-confirm
      />
    </footer>
  `,
  host: {
    class: 'flex flex-col min-w-80 w-120 max-w-full',
    '[attr.role]': "ref ? null : 'dialog'",
    '[attr.aria-labelledby]': "ref ? null : id + '-title'",
    '[attr.aria-busy]': 'isSubmitting()',
  },
})
export class FormAskComponent {
  private static next = 0;

  protected readonly ref = inject(OverlayRef<boolean>, { optional: true });
  protected readonly texts = inject(TextsService);
  /** Its own: a form of input alone, which the elements inside draw their fields from. */
  protected readonly form = inject(FormService);
  /** The opener's submission; a dialog mounted without one answers no message. */
  private readonly send = inject(ASK_SUBMIT, { optional: true });
  /** The values the opener opens the dialog with; a dialog given none opens empty. */
  private readonly given = inject(ASK_VALUE, { optional: true });
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);

  /** The id the title carries, so the dialog can be named by it. */
  protected readonly id = `ui-ask-${++FormAskComponent.next}`;

  readonly model = model<FormAskModel | null>();

  /** The title the opener gave, the button's label; the platform's `Confirm` without one. */
  protected readonly title = computed(() => this.model()?.title || this.texts.of('Confirm'));

  /** What the dialog's form follows: the elements of the model and the values the opener gave. */
  private readonly input = computed<FormInput>(() => ({ elements: this.model()?.elements ?? [], value: this.given ?? {} }));

  /** Whether the opener's submission has not answered yet. */
  protected readonly isSubmitting = signal(false);

  constructor() {
    this.ref?.name({ role: 'dialog', labelledBy: `${this.id}-title` });
    // A refusal must find its fields: the dialog does not close while its submission is in flight
    const stopClose = this.ref?.registerCloseParticipant(() => !this.isSubmitting());
    inject(DestroyRef).onDestroy(() => stopClose?.());

    this.form.setInput(this.input);
  }

  protected spanOf(element: FormElementDto): string {
    return spanClassOf(element);
  }

  protected cancel(): void {
    if (this.isSubmitting()) {
      return;
    }
    void this.ref?.close(false);
  }

  /** Hands the values to the opener and waits: its messages stay on the fields, none closes the dialog. */
  protected async submit(): Promise<void> {
    if (this.isSubmitting()) {
      return;
    }
    const messages = await this.answerOf(inputOf(this.form.value(), this.model()?.elements ?? []));
    if (messages.length) {
      this.form.setMessages(messages);
      afterNextRender(() => this.focusFirstInvalid(), { injector: this.injector });
    } else {
      await this.ref?.close(true);
    }
  }

  /** The opener's answer to `value`, the dialog busy until it comes, however it comes. */
  private async answerOf(value: Readonly<Record<string, unknown>>): Promise<readonly MessageDto[]> {
    this.isSubmitting.set(true);
    try {
      return (await this.send?.(value)) ?? [];
    } finally {
      this.isSubmitting.set(false);
    }
  }

  /** After a refusal the first invalid field takes focus, so the user starts where the server did. */
  private focusFirstInvalid(): void {
    this.host.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }
}

/** What the dialog submits: `value` at the paths of `elements` alone, so the caption a reference keeps beside its key stays behind. */
function inputOf(value: Readonly<Record<string, unknown>>, elements: readonly FormElementDto[]): Record<string, unknown> {
  return Object.fromEntries(elements.map(element => [element.path, value[element.path] ?? null]));
}
