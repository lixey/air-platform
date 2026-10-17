import { Component, input, model } from '@angular/core';
import { type FormValueControl, transformedValue } from '@angular/forms/signals';
import { fieldClasses } from '../theme/css-classes';
import { caretAfterDigits, phoneDigits, phoneText, phoneValue } from './phone-mask';
import { RECORD_AUTOCOMPLETE, RECORD_NAME } from './record-autocomplete';
import { UiControlDirective } from './ui-control.directive';

/**
 * A phone box implementing FormValueControl: it writes the digits as they are typed by the mask whose fixed prefix they
 * start with, keeps the caret after the last digit typed, and writes `+` and the digits, or null for an empty box.
 */
@Component({
  selector: 'ui-phone-box',
  imports: [UiControlDirective],
  template: `
    <input
      [uiControl]="this"
      [uiControlValue]="rawValue()"
      type="tel"
      autocomplete="${RECORD_AUTOCOMPLETE}"
      name="${RECORD_NAME}"
      [class]="fieldClasses(invalid())"
      [placeholder]="placeholder()"
      (input)="onInput($event)"
    />
  `,
})
export class PhoneBoxComponent implements FormValueControl<string | null> {
  readonly value = model<string | null>(null);

  readonly id = input('');
  readonly placeholder = input('');
  readonly disabled = input(false);
  readonly readonly = input(false);
  readonly required = input(false);
  readonly invalid = input(false);
  /** The masks a number is written by, `#` a digit (`+380 (##) ###-##-##`); none writes `+` and the digits. */
  readonly masks = input<readonly string[]>([]);

  protected readonly fieldClasses = fieldClasses;

  protected readonly rawValue = transformedValue(this.value, {
    parse: (text: string) => ({ value: phoneValue(text) }),
    format: (phone: string | null) => phoneText(phoneDigits(phone ?? ''), this.masks()),
  });

  /**
   * Writes what was typed by its mask and puts the caret after as many digits as stood before it. The element is given
   * the text here, before the control directive draws it, so the directive finds it drawn and leaves the caret alone.
   */
  protected onInput(event: Event): void {
    const box = event.target as HTMLInputElement;
    const typed = box.value;
    const digitsBefore = phoneDigits(typed.slice(0, box.selectionStart ?? typed.length)).length;
    const text = phoneText(phoneDigits(typed), this.masks());
    this.rawValue.set(text);
    box.value = text;
    const caret = caretAfterDigits(text, digitsBefore);
    box.setSelectionRange(caret, caret);
  }
}
