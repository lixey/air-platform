import { Component, computed, inject, input, model } from '@angular/core';
import { type FormValueControl, transformedValue } from '@angular/forms/signals';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { fieldClasses, INNER_CONTROL_HOVER, NO_SPIN_ARROWS, SEGMENT_FIRST, SEGMENT_LAST } from '../theme/css-classes';
import { FIELD_LOOK } from './field-look';
import { canStep, steppedNumber } from './number-step';
import { RECORD_AUTOCOMPLETE, RECORD_NAME } from './record-autocomplete';
import { UiControlDirective } from './ui-control.directive';

/**
 * A stepper's button: inside the frame at its full height, its icon at 16 pixels, hovering inside the frame, its icon
 * faded while disabled.
 */
const STEP_BUTTON =
  `absolute inset-y-px flex items-center justify-center text-muted ${INNER_CONTROL_HOVER} disabled:[&>i]:opacity-50 ` +
  'cursor-pointer disabled:cursor-default [&>i>svg]:h-4 [&>i>svg]:w-4';

/** The minus's line on its right, towards the text; a pixel on its outer side keeps its icon where it stood. */
const MINUS_LINE = 'border-r border-field-edge pl-px';

/** The plus's line on its left, towards the text; a pixel on its outer side keeps its icon where it stood. */
const PLUS_LINE = 'border-l border-field-edge pr-px';

/**
 * The stepper's buttons and the input's room by the look: 34 pixels inside a box's border, 24 inside a cell's frame,
 * each parted from the text by its line; the text keeps after the line the gap it keeps after the edge without them.
 */
const STEPPER_LOOK = {
  box: {
    minus: `${STEP_BUTTON} ${MINUS_LINE} w-8.5 left-px ${SEGMENT_FIRST}`,
    plus: `${STEP_BUTTON} ${PLUS_LINE} w-8.5 right-px ${SEGMENT_LAST}`,
    room: 'pl-10.5! pr-10.5!',
  },
  cell: {
    minus: `${STEP_BUTTON} ${MINUS_LINE} w-6 left-px`,
    plus: `${STEP_BUTTON} ${PLUS_LINE} w-6 right-px`,
    room: 'pl-8! pr-8!',
  },
} as const;

/**
 * A number box, left aligned in tabular figures, implementing FormValueControl. Its native input draws no spin
 * arrows and the wheel changes no number while it holds the focus. `ArrowUp` and `ArrowDown` are its own, not the
 * browser's: with or without the stepper they step the value as a press of its plus or minus does, never to the
 * browser's step base. While `steppable` it draws a minus inside the box on the left and a plus on the right, after
 * the input in the document and out of the tab order, which step the value within `min` and `max` and leave the focus
 * where it was; each is parted from the text by a `field-edge` line, and in a cell they are narrower than in a box.
 */
@Component({
  selector: 'ui-number-box',
  imports: [UiControlDirective, UiIconDirective],
  template: `
    <input
      [uiControl]="this"
      [uiControlValue]="formattedValue()"
      type="number"
      [attr.step]="'any'"
      [attr.min]="min()"
      [attr.max]="max()"
      [class]="inputClasses()"
      [placeholder]="placeholder()"
      autocomplete="${RECORD_AUTOCOMPLETE}"
      name="${RECORD_NAME}"
      (input)="onInput($event)"
      (wheel)="keepNumberOnWheel($event)"
      (keydown)="stepByKey($event)"
    />
    @if (steppable()) {
      <button
        type="button"
        tabindex="-1"
        [class]="stepper.minus"
        [attr.aria-label]="texts.of('NumberDecrease')"
        [disabled]="!canDecrease()"
        (mousedown)="$event.preventDefault()"
        (click)="stepBy(-1)"
      >
        <i icon="minus" aria-hidden="true"></i>
      </button>
      <button
        type="button"
        tabindex="-1"
        [class]="stepper.plus"
        [attr.aria-label]="texts.of('NumberIncrease')"
        [disabled]="!canIncrease()"
        (mousedown)="$event.preventDefault()"
        (click)="stepBy(1)"
      >
        <i icon="plus" aria-hidden="true"></i>
      </button>
    }
  `,
  host: { '[class]': 'hostClasses' },
})
export class NumberBoxComponent implements FormValueControl<number | null> {
  protected readonly texts = inject(TextsService);
  private readonly look = inject(FIELD_LOOK, { optional: true }) ?? 'box';

  readonly value = model<number | null>(null);

  readonly id = input('');
  readonly placeholder = input('');
  readonly disabled = input(false);
  readonly readonly = input(false);
  readonly required = input(false);
  readonly invalid = input(false);
  readonly min = input<number | undefined>(undefined);
  readonly max = input<number | undefined>(undefined);
  /** The increment the box steps by; any step when unset. */
  readonly step = input<number | null | undefined>(undefined);
  /** Whether the box draws its stepper: a minus and a plus inside it. */
  readonly steppable = input(false);

  /** The host holds the stepper's buttons over the box; in a cell it is as high as the cell. */
  protected readonly hostClasses = this.look === 'cell' ? 'relative block h-full' : 'relative block';
  /** The stepper's buttons and the input's room in this box's look. */
  protected readonly stepper = STEPPER_LOOK[this.look];

  protected readonly inputClasses = computed(() => {
    const room = this.steppable() ? ` ${this.stepper.room}` : '';
    return `${fieldClasses(this.invalid(), this.look)} tabular-nums ${NO_SPIN_ARROWS}${room}`;
  });

  /** Whether a press steps nothing: a disabled or read-only field. */
  private readonly isFixed = computed(() => this.disabled() || this.readonly());
  protected readonly canDecrease = computed(() => this.canStepTo(-1));
  protected readonly canIncrease = computed(() => this.canStepTo(1));
  protected readonly formattedValue = computed(() => {
    const v = this.value();
    return v == null ? '' : Object.is(v, -0) ? '-0' : String(v);
  });

  protected readonly rawValue = transformedValue(this.value, {
    parse: (val: string) => {
      if (val === '') {
        return { value: null };
      }
      const num = Number(val);
      if (Number.isNaN(num)) {
        return { error: { kind: 'parse', message: this.texts.of('FormBindingValue') } };
      }
      return { value: num };
    },
    format: (val: number | null) => (val == null ? '' : String(val)),
  });

  /** A press of the stepper: the value less or plus the step, held within the bounds; nothing on a fixed field. */
  protected stepBy(direction: 1 | -1): void {
    if (!this.canStepTo(direction)) {
      return;
    }
    this.value.set(steppedNumber(this.value(), direction, this.step(), this.bound(this.min()), this.bound(this.max())));
  }

  /**
   * `ArrowUp` and `ArrowDown` with no Alt, Ctrl or Meta: the default prevented, the value stepped as a press of the
   * stepper steps it.
   */
  protected stepByKey(event: KeyboardEvent): void {
    if ((event.key !== 'ArrowUp' && event.key !== 'ArrowDown') || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    event.preventDefault();
    this.stepBy(event.key === 'ArrowUp' ? 1 : -1);
  }

  /** The wheel over the focused input changes no number; over an input without the focus it scrolls as ever. */
  protected keepNumberOnWheel(event: WheelEvent): void {
    const input = event.target as HTMLInputElement;
    if (input.ownerDocument.activeElement === input) {
      event.preventDefault();
    }
  }

  protected onInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    if (target.validity?.badInput) {
      this.rawValue.set('NaN');
      return;
    }
    this.rawValue.set(target.value);
  }

  /** Whether a press toward `direction` changes the value: a field that takes input, short of its bound. */
  private canStepTo(direction: 1 | -1): boolean {
    return !this.isFixed() && canStep(this.value(), direction, this.bound(this.min()), this.bound(this.max()));
  }

  /** A bound as the stepper reads it: none for a bound a host bound to null. */
  private bound(value: number | null | undefined): number | undefined {
    return value ?? undefined;
  }
}
