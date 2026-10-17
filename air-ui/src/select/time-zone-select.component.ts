import { booleanAttribute, Component, computed, input, linkedSignal, model } from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';
import type { SelectOption } from './select-option';
import type { SelectSource } from './select-source';
import { SelectComponent } from './select.component';

/** The position of the empty choice, first among the choices. */
const EMPTY_CHOICE = 0;

/** The position a value outside the zones is shown at: no choice stands there. */
const NOT_OFFERED = -1;

/**
 * The time zone control: the library's select over the zones the server offers, searchable, each labelled by its IANA
 * id, the empty choice first, labelled by `inherit`, what no zone of its own resolves to, which is also the text of an
 * empty value. As a form control its value is the zone's id, `null` for the empty choice: the select picks by the
 * choices' positions and the control writes the id itself. A value outside the zones is shown by its id. While
 * readonly or disabled it opens nothing and writes nothing, as the select.
 */
@Component({
  selector: 'ui-time-zone-select',
  imports: [SelectComponent],
  template: `
    <ui-select
      [model]="source()"
      [picked]="picked()"
      [placeholder]="inherit()"
      [clearable]="clearable()"
      [readonly]="readonly()"
      [disabled]="disabled()"
      [invalid]="invalid()"
      (pickedChange)="pick($event)"
    />
  `,
  host: { class: 'block w-full min-w-0' },
})
export class TimeZoneSelectComponent implements FormValueControl<string | null> {
  /** The zone's IANA id; `null` for none, which takes the zone `inherit` names. */
  readonly value = model<string | null>(null);

  /** The zones offered, in the server's order. */
  readonly zones = input<readonly string[]>([]);
  /** The label of the empty choice: what the empty value resolves to. */
  readonly inherit = input('');
  /** What is picked, named to a screen reader and in the select's card. */
  readonly label = input('');
  readonly clearable = input(true, { transform: booleanAttribute });
  readonly readonly = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly invalid = input(false);

  /** The choices by position: the empty one first, then each zone. */
  private readonly options = computed<readonly SelectOption[]>(() => [
    { id: EMPTY_CHOICE, label: this.inherit() },
    ...this.zones().map((zone, index) => ({ id: index + 1, label: zone })),
  ]);

  protected readonly source = computed<SelectSource>(() => ({ label: this.label(), options: this.options() }));

  /** What the select shows picked: the zone of the value, by its id, and nothing for no value. */
  protected readonly picked = linkedSignal<readonly SelectOption[]>(() => {
    const zone = this.value();
    if (zone === null) {
      return [];
    }
    const at = this.zones().indexOf(zone);
    return [{ id: at < 0 ? NOT_OFFERED : at + 1, label: zone }];
  });

  /** A choice picked: its zone's id written, `null` for the empty choice or a clear. */
  protected pick(options: readonly SelectOption[]): void {
    const at = options[0]?.id ?? EMPTY_CHOICE;
    const zone = at > EMPTY_CHOICE ? (this.zones()[at - 1] ?? null) : null;
    this.value.set(zone);
    // A new list for no zone as well: the select that showed the empty choice picked shows the empty value again
    this.picked.set(zone === null ? [] : [...options]);
  }
}
