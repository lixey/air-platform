import { Component, computed, inject, input, model, signal } from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';
import type { OptionDto } from '../core/generated/contracts';
import { CHECKBOX_CLASSES, FIELD_FOCUS_WITHIN, INVALID_EDGE } from '../theme/css-classes';
import { FIELD_DESCRIPTION } from './field-description';
import { UiControlDirective, type UiControlState } from './ui-control.directive';

/** A set of checkboxes editing an array of checked IDs, implementing FormValueControl. */
@Component({
  selector: 'ui-checkbox-set',
  imports: [UiControlDirective],
  template: `
    <div
      [class]="
        'flex flex-wrap items-center gap-x-6 gap-y-1 min-h-9 pl-1.5 pr-2 py-1 rounded-lg border bg-transparent ${FIELD_FOCUS_WITHIN} ' +
        (invalid() ? '${INVALID_EDGE}' : 'border-field-edge')
      "
      role="group"
      [attr.aria-label]="label()"
      [attr.aria-describedby]="description?.() ?? null"
    >
      @for (option of options(); track option.id) {
        <label class="flex items-center gap-2 text-base cursor-pointer" [class.text-muted]="disabled()" [attr.title]="option.title ?? null">
          <input
            type="checkbox"
            class="${CHECKBOX_CLASSES}"
            [uiControl]="box"
            [uiControlValue]="checkedSet().has(option.id)"
            (change)="toggle(option.id, $event)"
          />
          <span>{{ option.name }}</span>
        </label>
      } @empty {
        <span class="text-base text-muted">—</span>
      }
    </div>
  `,
})
export class CheckboxSetComponent implements FormValueControl<readonly number[] | null> {
  readonly value = model<readonly number[] | null>(null);

  readonly options = input<readonly OptionDto[]>([]);
  readonly disabled = input(false);
  readonly readonly = input(false);
  readonly required = input(false);
  readonly invalid = input(false);
  readonly label = input<string | null>(null);

  /** What describes the set: its group carries it, not each box. */
  protected readonly description = inject(FIELD_DESCRIPTION, { optional: true });

  /** A box of the set: no id, no native required, which would ask for every box to be checked, and no description of its own. */
  protected readonly box: UiControlState = {
    disabled: this.disabled,
    readonly: this.readonly,
    invalid: this.invalid,
    describedBy: signal(null),
  };

  protected readonly checkedSet = computed(() => {
    const val = this.value();
    return new Set(Array.isArray(val) ? val : []);
  });

  protected toggle(id: number, event: Event): void {
    const on = (event.target as HTMLInputElement).checked;
    const next = new Set(this.checkedSet());
    if (on) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.value.set([...next]);
  }
}
