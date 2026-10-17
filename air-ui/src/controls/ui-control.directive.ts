import { DestroyRef, Directive, effect, ElementRef, inject, input, type Signal } from '@angular/core';
import { FIELD_DESCRIPTION } from './field-description';

/**
 * What the directive reads of the control it serves. The state inputs stay on the control component, because that
 * is where a form field writes them; a control that is a group of boxes leaves out what belongs to no single box.
 */
export interface UiControlState {
  readonly id?: Signal<string>;
  readonly disabled: Signal<boolean>;
  readonly readonly: Signal<boolean>;
  readonly required?: Signal<boolean>;
  readonly invalid: Signal<boolean>;
  /**
   * The ids that describe this element, where the control says so itself: a box of a group, which its group's
   * description covers, or a tree's node with its note. Without it the element is described by its form item.
   */
  readonly describedBy?: Signal<string | null>;
}

type NativeControl = HTMLInputElement | HTMLTextAreaElement;

const GUARDED = ['input', 'change', 'click'] as const;

/**
 * Owns the native element of a control: reflects the control's state on it, draws the value the control shows, and
 * refuses input while the control is readonly or disabled — the element keeps what it showed, stays mounted, is not
 * disabled by readonly and keeps its focus. A refused event never reaches the control's own listeners.
 *
 * `<input [uiControl]="this" [uiControlValue]="rawValue()" (input)="rawValue.set($event.target.value)" />`
 */
@Directive({
  selector: 'input[uiControl], textarea[uiControl]',
  host: {
    '[attr.id]': 'control().id?.() || null',
    '[disabled]': 'control().disabled()',
    '[readOnly]': 'control().readonly()',
    '[required]': 'control().required?.() ?? false',
    '[attr.aria-readonly]': 'control().readonly() ? "true" : null',
    '[attr.aria-invalid]': 'control().invalid() ? "true" : null',
    '[attr.aria-describedby]': 'describedBy()',
  },
})
export class UiControlDirective {
  private readonly element = inject<ElementRef<NativeControl>>(ElementRef).nativeElement;
  private readonly description = inject(FIELD_DESCRIPTION, { optional: true });

  /** The control component this native element belongs to. */
  readonly control = input.required<UiControlState>({ alias: 'uiControl' });

  /** What the element shows: the text of a box, or whether a checkbox is checked. */
  readonly uiControlValue = input.required<string | boolean>();

  constructor() {
    effect(() => this.draw(this.uiControlValue()));

    // Capturing: at the target these run before the control's listeners, so a refused event stops here
    const guard = (event: Event) => this.guard(event);
    for (const type of GUARDED) {
      this.element.addEventListener(type, guard, true);
    }
    inject(DestroyRef).onDestroy(() => {
      for (const type of GUARDED) {
        this.element.removeEventListener(type, guard, true);
      }
    });
  }

  protected describedBy(): string | null {
    const control = this.control();
    return (control.describedBy ? control.describedBy() : this.description?.()) || null;
  }

  private guard(event: Event): void {
    const control = this.control();
    if (!control.readonly() && !control.disabled()) {
      return;
    }
    if (event.type === 'click') {
      // A click only changes a checkbox; refusing it leaves the box as it was and the focus where it is
      if (typeof this.uiControlValue() === 'boolean') {
        event.preventDefault();
      }
      return;
    }
    this.draw(this.uiControlValue());
    event.stopImmediatePropagation();
  }

  private draw(shown: string | boolean): void {
    if (typeof shown === 'boolean') {
      (this.element as HTMLInputElement).checked = shown;
    } else if (this.element.value !== shown) {
      this.element.value = shown;
    }
  }
}
