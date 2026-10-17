import { Component, computed, inject, input, model, signal } from '@angular/core';
import { type FormValueControl, transformedValue } from '@angular/forms/signals';
import { fieldClasses } from '../theme/css-classes';
import { type AmountFormat, type AmountKind, amountUnitText, groupedAmountText, parseAmount, plainAmountText } from './amount-text';
import { FIELD_DESCRIPTION } from './field-description';
import { FIELD_LOOK } from './field-look';
import { RECORD_AUTOCOMPLETE, RECORD_NAME } from './record-autocomplete';
import { UiControlDirective, type UiControlState } from './ui-control.directive';

/**
 * An amount box — money or a percent — implementing FormValueControl: left aligned in tabular figures, plain while it
 * has the focus and grouped by its culture once the focus leaves, with the sign of the currency or `%` beside it. It
 * reads the culture's decimal sign, and `.` as one only where the culture does not group with it (in `de-DE` a `.` is
 * a group sign, so `12.50` is text it cannot read), and drops spaces and the group sign between groups of three
 * digits; it writes the number typed, never rounded, or null for an empty box, and reports text it cannot read — a
 * fraction longer than its digits among it — as the number box does. Its `aria-describedby` names the field's help and
 * messages, then the unit beside the amount.
 */
@Component({
  selector: 'ui-amount-box',
  imports: [UiControlDirective],
  template: `
    <input
      [uiControl]="this"
      [uiControlValue]="rawValue()"
      type="text"
      inputmode="decimal"
      autocomplete="${RECORD_AUTOCOMPLETE}"
      name="${RECORD_NAME}"
      [class]="inputClasses()"
      [placeholder]="placeholder()"
      (focus)="showPlain()"
      (blur)="showGrouped()"
      (input)="onInput($event)"
    />
    @if (unit()) {
      <span class="shrink-0 text-muted" [id]="unitId">{{ unit() }}</span>
    }
  `,
  host: { '[class]': 'hostClasses' },
})
export class AmountBoxComponent implements FormValueControl<number | null>, UiControlState {
  private static next = 0;

  private readonly description = inject(FIELD_DESCRIPTION, { optional: true });
  private readonly look = inject(FIELD_LOOK, { optional: true }) ?? 'box';

  readonly value = model<number | null>(null);

  readonly id = input('');
  readonly placeholder = input('');
  readonly disabled = input(false);
  readonly readonly = input(false);
  readonly required = input(false);
  readonly invalid = input(false);
  /** The culture the box writes and reads the amount in. */
  readonly locale = input.required<string>();
  readonly kind = input<AmountKind>('money');
  /** The ISO 4217 code of money's currency; none for a grouped number. */
  readonly currency = input<string | null>(null);
  /** The fraction digits the amount keeps: its field's scale; two when unset. */
  readonly digits = input<number | null | undefined>(undefined);

  /** Whether the box shows the amount grouped: from the start and after the focus leaves text it could read. */
  private readonly isGrouped = signal(true);

  /** What the amount is and how it is written: the box's kind, digits and currency. */
  private readonly format = computed<AmountFormat>(() => ({ kind: this.kind(), digits: this.digits(), currency: this.currency() }));

  protected readonly unit = computed(() => amountUnitText(this.locale(), this.format()));

  /** The id of the unit beside the amount, which the box's description names. */
  protected readonly unitId = `ui-amount-unit-${++AmountBoxComponent.next}`;

  /** What describes the box: the field's help and messages, then its unit while one is drawn; `null` for neither. */
  readonly describedBy = computed(() => [this.description?.(), this.unit() ? this.unitId : null].filter(Boolean).join(' ') || null);

  /** The box and its unit in a row; in a cell as high as the cell, the unit inside the cell's right padding. */
  protected readonly hostClasses = this.look === 'cell' ? 'flex items-center h-full pr-2' : 'flex items-center gap-2';

  protected readonly inputClasses = computed(() => `${fieldClasses(this.invalid(), this.look)} tabular-nums`);

  protected readonly rawValue = transformedValue(this.value, {
    parse: (text: string) => {
      const amount = parseAmount(text, this.locale(), this.digits());
      return Number.isNaN(amount) ? { error: { kind: 'parse' } } : { value: amount };
    },
    format: (amount: number | null) => this.textOf(amount),
  });

  protected showPlain(): void {
    this.isGrouped.set(false);
  }

  /** Groups the amount as the focus leaves, unless the box holds text it cannot read, which stays for the user to fix. */
  protected showGrouped(): void {
    if (this.rawValue.parseErrors().length === 0) {
      this.isGrouped.set(true);
    }
  }

  protected onInput(event: Event): void {
    this.rawValue.set((event.target as HTMLInputElement).value);
  }

  /** The text of an amount: grouped or plain as the box stands; empty for none. */
  private textOf(amount: number | null): string {
    if (amount === null) {
      return '';
    }
    return this.isGrouped() ? groupedAmountText(amount, this.locale(), this.format()) : plainAmountText(amount, this.locale());
  }
}
