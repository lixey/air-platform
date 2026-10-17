import { Component, input, model } from '@angular/core';
import type { FormCheckboxControl } from '@angular/forms/signals';
import { CHECKBOX_CLASSES, FIELD_FOCUS_WITHIN, FIELD_HOVER, INVALID_EDGE } from '../theme/css-classes';
import { UiControlDirective } from './ui-control.directive';

/** A check box implementing the Signal Forms checkbox control contract. */
@Component({
  selector: 'ui-checkbox',
  imports: [UiControlDirective],
  template: `
    <label
      [class]="
        enclosed()
          ? 'flex items-center gap-2 h-9 min-h-9 pl-1.5 pr-2 rounded-lg border bg-transparent text-base select-none ${FIELD_FOCUS_WITHIN} ' +
            (invalid() ? '${INVALID_EDGE}' : 'border-field-edge ${FIELD_HOVER}')
          : 'flex items-center gap-2 min-h-9 text-base select-none'
      "
      [class.cursor-pointer]="!readonly() && !disabled()"
      [class.cursor-default]="readonly()"
      [class.text-muted]="disabled()"
      [class.opacity-60]="disabled()"
      [class.pointer-events-none]="disabled()"
    >
      <input
        #box
        [uiControl]="this"
        [uiControlValue]="checked() === true"
        type="checkbox"
        class="${CHECKBOX_CLASSES}"
        (change)="checked.set(box.checked)"
      />
      <ng-content />
    </label>
  `,
})
export class CheckboxComponent implements FormCheckboxControl {
  readonly checked = model<boolean>(false);
  readonly disabled = input(false);
  readonly readonly = input(false);
  readonly required = input(false);
  readonly invalid = input(false);
  readonly enclosed = input(true);
  readonly id = input('');
}
