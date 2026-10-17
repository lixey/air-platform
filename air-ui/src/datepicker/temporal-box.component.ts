import {
  booleanAttribute,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  input,
  model,
  untracked,
  viewChild,
} from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';
import { LocaleService } from '../app/locale.service';
import { FIELD_DESCRIPTION } from '../controls/field-description';
import { FIELD_LOOK } from '../controls/field-look';
import { resultGate } from '../controls/result-gate';
import { type TemporalKind, temporalText, temporalValue } from '../controls/temporal-text';
import type { UiIcon, UiTemporalType } from '../core/generated/contracts';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { fieldClasses } from '../theme/css-classes';
import { DatePickerService } from './date-picker.service';
import { formatTemporalText } from './date-text';

/** The keys of the closed combobox that open the picker; `Alt+ArrowDown` is `ArrowDown` with `altKey`. */
const OPENING_KEYS = new Set(['Enter', ' ', 'ArrowDown']);

/** The keys of the closed combobox that empty it, where the host allows no value. */
const CLEARING_KEYS = new Set(['Delete', 'Backspace']);

/**
 * The temporal control: a calendar date, a date and time or a time of day, over the wire representation. It is a
 * combobox that shows the value in the locale's numeric format, 24-hour, an instant on the user's clock
 * (`LocaleService.zone`), and opens the date picker on a click, on
 * `Enter`, `Space`, `ArrowDown` and `Alt+ArrowDown`, and on a printable key, which the picker's text starts with;
 * `Delete` and `Backspace` write no value where the host allows it. `Enter` stops at the control, so a menu hosting
 * it does not take it as its own. The wire value is the temporal rules' (`controls/temporal-text.ts`) for what the
 * picker commits over the value held, so untouched precision survives. The combobox is a button, so a host's
 * `<label for>` names it; while readonly or disabled it opens nothing and writes nothing.
 */
@Component({
  selector: 'ui-temporal-box',
  imports: [UiIconDirective],
  providers: [DatePickerService],
  template: `
    <button
      #combobox
      type="button"
      role="combobox"
      aria-haspopup="dialog"
      [attr.id]="id() || null"
      [class]="classes()"
      [disabled]="disabled()"
      [attr.aria-expanded]="picker.isOpen()"
      [attr.aria-controls]="picker.popupId()"
      [attr.aria-describedby]="description?.() ?? null"
      [attr.aria-readonly]="readonly() || null"
      [attr.aria-required]="required() || null"
      [attr.aria-invalid]="invalid() || null"
      (click)="open($event)"
      (keydown)="onKeyDown($event)"
    >
      <span class="flex-1 min-w-0 truncate" [class.text-muted]="!text()">{{ text() || placeholder() }}</span>
      <i [icon]="icon()" class="shrink-0 text-muted [&>svg]:size-4 [&>svg]:m-0" aria-hidden="true"></i>
    </button>
  `,
  host: { class: 'block w-full min-w-0', '[class.h-full]': 'look === "cell"', '[class.opacity-60]': 'disabled()' },
})
export class TemporalBoxComponent implements FormValueControl<string | null> {
  protected readonly picker = inject(DatePickerService);
  private readonly locale = inject(LocaleService);
  private readonly injector = inject(Injector);
  protected readonly description = inject(FIELD_DESCRIPTION, { optional: true });
  /** How the combobox is drawn: its own box, or the whole of a cell whose editor it is. */
  protected readonly look = inject(FIELD_LOOK, { optional: true }) ?? 'box';

  /** The wire value: a date, a date and time or a time of day as the field's temporal type writes it; `null` for none. */
  readonly value = model<string | null>(null);

  readonly id = input('');
  readonly kind = input<TemporalKind>('date');
  readonly temporalType = input<UiTemporalType | null>(null);
  /** The minutes between two options of the picker's minutes column; 5 when absent. */
  readonly minuteStep = input<number | null>(null);
  /** Whether the control may write no value: `Delete`, `Backspace` and the picker's `Clear`. */
  readonly clearable = input(true, { transform: booleanAttribute });
  readonly placeholder = input('');
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly readonly = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly invalid = input(false);
  private readonly combobox = viewChild.required<ElementRef<HTMLButtonElement>>('combobox');

  protected readonly refusesInput = computed(() => this.readonly() || this.disabled());

  /** Whether a commit of the picker this control opened is written: not once the control refuses input. */
  private readonly acceptsResult = resultGate(this.refusesInput);

  /** What the temporal rules read of the field: its type and its temporal type. */
  private readonly element = computed(() => ({ type: this.kind(), temporalType: this.temporalType() }));

  /** The editor text of the value (`yyyy-MM-dd`, `yyyy-MM-ddTHH:mm`, `HH:mm`): where the picker starts. */
  private readonly editorText = computed(() => temporalText(this.value(), this.element(), this.locale.zone()));

  /** The value as the combobox shows it: the locale's numeric date, the time 24-hour. */
  protected readonly text = computed(() => formatTemporalText(this.editorText(), this.kind(), this.locale.current()));

  protected readonly icon = computed<UiIcon>(() => (this.kind() === 'time' ? 'clock' : 'calendar'));

  protected readonly classes = computed(
    () => `${fieldClasses(this.invalid(), this.look)} flex items-center gap-2 text-left cursor-pointer disabled:cursor-default`,
  );

  constructor() {
    const subscription = this.picker.committed.subscribe(text => {
      if (this.acceptsResult()) {
        this.write(temporalValue(text, untracked(this.value), untracked(this.element), untracked(this.locale.zone)));
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  protected open(event: Event, seed?: string): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.refusesInput()) {
      return;
    }
    this.acceptsResult.set(true);
    this.picker.open(
      { kind: this.kind(), text: this.editorText(), minuteStep: this.minuteStep(), clearable: this.clearable(), seed },
      this.combobox().nativeElement,
      this.injector,
    );
  }

  /** The keys of the closed combobox: those that open the picker, a printable key that opens it seeded, and those that empty it. */
  protected onKeyDown(event: KeyboardEvent): void {
    if (OPENING_KEYS.has(event.key)) {
      this.open(event);
    } else if (isPrintable(event)) {
      this.open(event, event.key);
    } else if (CLEARING_KEYS.has(event.key) && this.clearable() && this.value() !== null) {
      event.preventDefault();
      this.write(null);
    }
  }

  private write(value: string | null): void {
    if (this.refusesInput()) {
      return;
    }
    this.value.set(value);
  }
}

/** A key that types a character: one character, no command modifier. */
function isPrintable(event: KeyboardEvent): boolean {
  return event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey;
}
