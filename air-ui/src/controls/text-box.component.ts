import { afterRenderEffect, Component, type ElementRef, inject, input, model, signal, viewChild } from '@angular/core';
import { type FormValueControl, transformedValue } from '@angular/forms/signals';
import { fieldClasses } from '../theme/css-classes';
import { FIELD_LOOK } from './field-look';
import { RECORD_AUTOCOMPLETE, RECORD_NAME } from './record-autocomplete';
import { UiControlDirective } from './ui-control.directive';

/**
 * A text box for text, password, email, a site's address or textarea, implementing FormValueControl. The text area opens at three lines
 * and grows with its text, without a limit and without a scroll of its own.
 */
@Component({
  selector: 'ui-text-box',
  imports: [UiControlDirective],
  template: `
    @if (kind() === 'textarea') {
      <textarea
        #area
        [uiControl]="this"
        [uiControlValue]="rawValue()"
        [attr.autocomplete]="autocomplete() ?? '${RECORD_AUTOCOMPLETE}'"
        [attr.name]="autocomplete() === null ? '${RECORD_NAME}' : name()"
        rows="3"
        [class]="fieldClasses(invalid(), look) + ' py-2 resize-none overflow-hidden'"
        [placeholder]="placeholder()"
        [attr.minlength]="minLength()"
        [attr.maxlength]="maxLength()"
        (input)="onInput($event)"
      ></textarea>
    } @else {
      <input
        [uiControl]="this"
        [uiControlValue]="rawValue()"
        [attr.name]="autocomplete() === null ? '${RECORD_NAME}' : name()"
        [type]="kind()"
        [class]="fieldClasses(invalid(), look)"
        [placeholder]="placeholder()"
        [attr.minlength]="minLength()"
        [attr.maxlength]="maxLength()"
        [attr.autocomplete]="autocomplete() ?? (kind() === 'password' ? 'new-password' : '${RECORD_AUTOCOMPLETE}')"
        (input)="onInput($event)"
      />
    }
  `,
})
export class TextBoxComponent implements FormValueControl<string | null> {
  /** How the box is drawn: its own box, or the whole of a cell whose editor it is. */
  protected readonly look = inject(FIELD_LOOK, { optional: true }) ?? 'box';

  readonly value = model<string | null>(null);

  readonly id = input('');
  readonly name = input('');
  readonly kind = input<'text' | 'password' | 'email' | 'url' | 'textarea'>('text');
  readonly allowsNull = input(true);
  readonly placeholder = input('');
  readonly autocomplete = input<string | null>(null);
  readonly disabled = input(false);
  readonly readonly = input(false);
  readonly required = input(false);
  readonly invalid = input(false);
  readonly minLength = input<number | undefined>(undefined);
  readonly maxLength = input<number | undefined>(undefined);

  private readonly area = viewChild<ElementRef<HTMLTextAreaElement>>('area');

  /** The width of the text area, which re-wraps its lines when a panel or the window resizes. */
  private readonly areaWidth = signal(0);

  protected readonly fieldClasses = fieldClasses;

  protected readonly rawValue = transformedValue(this.value, {
    parse: (val: string) => ({
      value: val === '' && this.allowsNull() ? null : val,
    }),
    format: (val: string | null) => val ?? '',
  });

  /** Follows the width of the text area while it is drawn. */
  private readonly watchAreaWidth = afterRenderEffect(onCleanup => {
    const area = this.area()?.nativeElement;
    if (!area || typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver(([entry]) => this.areaWidth.set(entry?.contentRect.width ?? 0));
    observer.observe(area);
    onCleanup(() => observer.disconnect());
  });

  /** Fits the text area's height to its text on every change of the text or of its width. */
  private readonly fitArea = afterRenderEffect({
    write: () => {
      this.rawValue();
      this.areaWidth();
      const area = this.area()?.nativeElement;
      if (area) {
        fitToText(area);
      }
    },
  });

  protected onInput(event: Event): void {
    this.rawValue.set((event.target as HTMLInputElement | HTMLTextAreaElement).value);
  }
}

/**
 * Sets the height of a text area to its text: `auto` first, so the three lines of `rows` are the least, then the
 * scroll height with the borders, which `box-sizing: border-box` counts in the height.
 */
function fitToText(area: HTMLTextAreaElement): void {
  area.style.height = 'auto';
  area.style.height = `${area.scrollHeight + area.offsetHeight - area.clientHeight}px`;
}
