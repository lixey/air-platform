import { booleanAttribute, Component, computed, DestroyRef, ElementRef, inject, Injector, input, model, viewChild } from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';
import { FIELD_DESCRIPTION } from '../controls/field-description';
import { FIELD_LOOK } from '../controls/field-look';
import { resultGate } from '../controls/result-gate';
import type { FormDataDto, OverlayModelDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { isFormData } from '../form/form-value';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { OverlayService } from '../overlay/overlay.service';
import { ScreenMutations } from '../screen/screen-mutations';
import { FIELD_FOCUS_WITHIN, FIELD_HOVER, INVALID_EDGE, PICK_CHIP } from '../theme/css-classes';
import { optionOfRow, type SelectOption } from './select-option';
import type { SelectSource } from './select-source';
import { SelectService } from './select.service';

/** The select's own box: the field's ground, edge, radius, hover and focus. */
const BOX = `flex min-h-9 w-full min-w-0 rounded-lg border bg-field text-base cursor-pointer ${FIELD_HOVER} ${FIELD_FOCUS_WITHIN}`;

/** The select's box as the whole of a cell whose editor it is: no edge, radius, ground or outline of its own. */
const CELL_BOX = 'flex h-full w-full min-w-0 border-0 rounded-none bg-transparent text-base text-ink cursor-pointer outline-none';

/**
 * The select as a control: a combobox that opens a dialog — the picked shown as its text, or as
 * chips beside it in multiple mode, a placeholder for none — in the field's box, with a control
 * that clears it when the host allows. A single select is one field line, whatever it holds; a
 * multiselect's chips stand inside its box, which grows with them to three rows and then scrolls
 * them, the clear control and the chevron on its first line. A click anywhere on the box opens the
 * select, and so do `Enter`, `Space`, `ArrowDown` and `Alt+ArrowDown` on the combobox; `Delete`
 * and `Backspace` clear it. The chips and the clear control stand beside the combobox, never
 * inside it, and out of the Tab order: one chip leaves by keyboard through the dialog, where
 * `Enter` opens it, its panel of the picked removes one and `Apply` commits. The picked is a
 * model the host reads and writes, so any host binds it without knowing the overlay; as a form
 * control its `value` is the key picked — the keys in multiple mode — which a form field binds
 * with its disabled, readonly and invalid states. While readonly or disabled it opens nothing and
 * writes nothing.
 */
@Component({
  selector: 'ui-select',
  imports: [UiIconDirective],
  providers: [SelectService],
  template: `
    <div [class]="boxClasses()">
      @if (multiple() && picked().length > 0) {
        <div class="flex flex-wrap gap-1 min-w-0 max-h-24 overflow-y-auto px-1 py-1">
          @for (option of picked(); track option.id) {
            <span
              class="inline-flex items-center gap-1 max-w-48 px-2 py-0.5 rounded text-sm leading-5 ${PICK_CHIP} truncate"
              [title]="option.label"
            >
              <span class="truncate">{{ option.label }}</span>
              <button
                type="button"
                tabindex="-1"
                class="hue-red hover:text-(--hue-ink)"
                [attr.aria-label]="texts.of('RemoveItem', option.label)"
                [attr.aria-disabled]="refusesInput()"
                (click)="remove($event, option)"
              >
                <i icon="close" aria-hidden="true"></i>
              </button>
            </span>
          }
        </div>
      }
      <div
        #combobox
        role="combobox"
        tabindex="0"
        aria-haspopup="dialog"
        class="flex-1 min-w-0 self-stretch flex items-center px-2 truncate outline-none"
        [class.text-link]="picked().length > 0"
        [class.text-muted]="picked().length === 0"
        [attr.aria-label]="model().label || null"
        [attr.aria-expanded]="select.isOpen()"
        [attr.aria-controls]="select.popupId()"
        [attr.aria-describedby]="description?.() ?? null"
        [attr.aria-invalid]="invalid() || null"
        [attr.aria-disabled]="refusesInput() || null"
        (keydown)="onKeyDown($event)"
      >
        @if (multiple() && picked().length > 0) {
          <span class="sr-only">{{ text() }}</span>
        } @else {
          <span class="truncate">{{ text() }}</span>
        }
      </div>
      @if (clearable() && picked().length > 0) {
        <button
          type="button"
          tabindex="-1"
          class="hue-red flex items-center justify-center size-5 mr-1 rounded-full text-muted hover:text-(--hue-ink) hover:scale-110 transition-all duration-150 hover:delay-150 cursor-pointer [&>svg]:size-3.5 [&_svg]:size-3.5"
          [class]="multiple() ? 'self-start mt-1.75' : ''"
          [title]="texts.of('Clear')"
          [attr.aria-label]="texts.of('Clear')"
          [attr.aria-disabled]="refusesInput()"
          (click)="clear($event)"
        >
          <i icon="close" class="[&>svg]:size-3.5" aria-hidden="true"></i>
        </button>
      }
      @if (createOverlay() && !refusesInput()) {
        <button
          type="button"
          tabindex="-1"
          class="hue-emerald flex items-center justify-center size-6 mr-0.5 rounded-md text-muted hover:text-(--hue-ink) hover:bg-(--hue-soft) transition-colors cursor-pointer [&>svg]:size-4 [&_svg]:size-4"
          [class]="multiple() ? 'self-start mt-1.25' : ''"
          [title]="texts.of('Add')"
          [attr.aria-label]="texts.of('Add')"
          (click)="create($event)"
        >
          <i icon="plus" class="[&>svg]:size-4" aria-hidden="true"></i>
        </button>
      }
      <i
        icon="chevron-bottom"
        class="shrink-0 px-2 text-muted [&>svg]:size-4 [&>svg]:m-0"
        [class]="multiple() ? 'self-start h-8.5 flex items-center' : ''"
        aria-hidden="true"
      ></i>
    </div>
  `,
  host: {
    class: 'block w-full',
    '[class.h-full]': 'look === "cell"',
    '[class.pointer-events-none]': 'disabled()',
    '[class.opacity-60]': 'disabled()',
    // A click anywhere in the field's box opens it; the keyboard has the combobox
    '(click)': 'open($event)',
  },
})
export class SelectComponent implements FormValueControl<unknown> {
  protected readonly texts = inject(TextsService);
  protected readonly select = inject(SelectService);
  private readonly overlay = inject(OverlayService);
  private readonly mutations = inject(ScreenMutations, { optional: true });
  private readonly injector = inject(Injector);
  protected readonly description = inject(FIELD_DESCRIPTION, { optional: true });
  /** How the box is drawn: its own, or the whole of a cell whose editor it is. */
  protected readonly look = inject(FIELD_LOOK, { optional: true }) ?? 'box';
  private readonly combobox = viewChild.required<ElementRef<HTMLElement>>('combobox');

  /** What to pick from and what it is called. */
  readonly model = input.required<SelectSource>();
  readonly multiple = input(false, { transform: booleanAttribute });
  readonly clearable = input(true, { transform: booleanAttribute });
  readonly readonly = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly invalid = input(false);
  readonly placeholder = input<string>('');
  readonly createOverlay = input<OverlayModelDto | null>(null);

  /** The picked: one option or none in single mode, any number in multiple. */
  readonly picked = model<readonly SelectOption[]>([]);

  /** What a form field binds: the key picked, `null` for none; the keys in multiple mode. */
  readonly value = model<unknown>(null);

  protected readonly refusesInput = computed(() => this.readonly() || this.disabled());

  /** The box's classes of the mode: one field line for a single select, the chips' rows from the top for a multiselect. */
  private readonly boxMode = computed(() => (this.multiple() ? 'items-start' : 'items-center h-9'));

  /** The box's classes: its own box with the edge of its validity, or a cell's, whose frame says it. */
  protected readonly boxClasses = computed(() =>
    this.look === 'cell'
      ? `${CELL_BOX} ${this.multiple() ? 'items-start' : 'items-center'}`
      : `${BOX} ${this.boxMode()} ${this.invalid() ? INVALID_EDGE : 'border-field-edge'}`,
  );

  /** Whether a result of the overlay this control opened is written: not once the control refuses input. */
  private readonly acceptsResult = resultGate(this.refusesInput);

  protected readonly text = computed(
    () =>
      this.picked()
        .map(o => o.label)
        .join(', ') || this.placeholder(),
  );

  constructor() {
    const subscription = this.select.committed.subscribe(options => {
      if (this.acceptsResult()) {
        this.write(options);
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  protected open(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.refusesInput()) {
      return;
    }
    this.acceptsResult.set(true);
    this.select.open(
      { model: this.model(), picked: this.picked(), multiple: this.multiple() },
      this.combobox().nativeElement,
      this.injector,
    );
  }

  /**
   * The keys of a combobox that opens a dialog: `Enter`, `Space`, `ArrowDown`
   * and `Alt+ArrowDown` open it, `Delete` and `Backspace` clear it.
   */
  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ' || event.key === 'ArrowDown') {
      this.open(event);
    } else if ((event.key === 'Delete' || event.key === 'Backspace') && this.clearable() && this.picked().length > 0) {
      event.preventDefault();
      this.clear(event);
    }
  }

  protected remove(event: Event, option: SelectOption): void {
    event.stopPropagation();
    this.write(this.picked().filter(o => o.id !== option.id));
  }

  protected clear(event: Event): void {
    event.stopPropagation();
    this.write([]);
  }

  protected create(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    const overlayModel = this.createOverlay();
    if (!overlayModel || this.refusesInput()) {
      return;
    }
    this.acceptsResult.set(true);
    const targetService = overlayModel.screen?.service;
    const ref = this.overlay.open(overlayModel, this.combobox().nativeElement, this.injector);
    const applySaved = (data: FormDataDto | null | undefined) => {
      if (!data || typeof data.id !== 'number' || data.id <= 0 || !this.acceptsResult()) {
        return;
      }
      const option = optionOfRow({ ...data.value, id: data.id }, this.model().labelPaths);
      const current = this.picked();
      const next = this.multiple()
        ? current.some(o => o.id === option.id)
          ? current.map(o => (o.id === option.id ? option : o))
          : [...current, option]
        : [option];
      this.write(next);
    };
    const unsubscribeMutation = this.mutations?.subscribe({
      accepts: e => e.operation === 'save' && (!targetService || e.service === targetService),
      refresh: e => applySaved(e?.data),
      invalidate: () => undefined,
    });
    const closedSub = ref.closed.subscribe(result => {
      unsubscribeMutation?.();
      closedSub.unsubscribe();
      if (isFormData(result)) {
        applySaved(result);
      }
    });
  }

  /** The host's picked, the key a form field binds and the service's picked, together, so the next opening starts from what is shown. */
  private write(options: readonly SelectOption[]): void {
    if (this.refusesInput()) {
      return;
    }
    this.picked.set([...options]);
    this.value.set(this.multiple() ? options.map(option => option.id) : (options[0]?.id ?? null));
    this.select.setPicked(options);
  }
}
