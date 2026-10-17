import { Component, computed, inject, input, model } from '@angular/core';
import { NumberBoxComponent } from '../../controls/number-box.component';
import type { TemporalKind } from '../../controls/temporal-text';
import { TextBoxComponent } from '../../controls/text-box.component';
import type { UiFieldType, UiTemporalType } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { TemporalBoxComponent } from '../../datepicker/temporal-box.component';
import { SelectComponent } from '../../select/select.component';
import { type FixedSelect, fixedSelectOf, pickOf, valueOfPick } from './fixed-select';

/** The control that holds a clause's value by the column's type; a type not named here is typed as text. */
const CONTROLS: Partial<Record<UiFieldType, 'checkbox' | 'number' | 'temporal'>> = {
  checkbox: 'checkbox',
  number: 'number',
  date: 'temporal',
  datetime: 'temporal',
  time: 'temporal',
};

/**
 * The value of one clause of a filter overlay, typed in the library's control for the column's type: the number
 * control, the temporal control for a date, a date and time or a time of day, the text control otherwise; a
 * checkbox's value is the library's select over any, yes and no, named by `label`. A date or a date and time is
 * written by `temporalType`. The value is what the control writes; `Enter` in it reaches whoever hosts it, except in
 * the temporal control and the select, where it opens the date picker or the select's overlay and stops there.
 */
@Component({
  selector: 'ui-grid-filter-value',
  imports: [NumberBoxComponent, SelectComponent, TemporalBoxComponent, TextBoxComponent],
  template: `
    @if (control() !== 'checkbox') {
      <label class="sr-only" [for]="id">{{ label() }}</label>
    }
    @switch (control()) {
      @case ('checkbox') {
        <ui-select
          [id]="id"
          [model]="flags().source"
          [clearable]="false"
          [picked]="flagPicked()"
          (pickedChange)="value.set(valueOfPick(flags(), $event) ?? null)"
        />
      }
      @case ('number') {
        <ui-number-box class="block w-full min-w-0" [id]="id" [value]="numberValue()" (valueChange)="value.set($event)" />
      }
      @case ('temporal') {
        <ui-temporal-box
          class="block w-full min-w-0"
          [id]="id"
          [kind]="temporalKind()"
          [temporalType]="temporalType()"
          [value]="textValue()"
          (valueChange)="value.set($event)"
        />
      }
      @default {
        <ui-text-box class="block w-full min-w-0" [id]="id" [value]="textValue()" (valueChange)="value.set($event)" />
      }
    }
  `,
  host: { class: 'flex flex-1 min-w-0' },
})
export class GridFilterValueComponent {
  private static next = 0;

  protected readonly texts = inject(TextsService);
  /** The id of the control, which the label names. */
  protected readonly id = `ui-grid-filter-value-${++GridFilterValueComponent.next}`;

  /** The column's type, which chooses the control. */
  readonly type = input<UiFieldType>('text');
  /** How a date or a date and time is written on the wire; `null` writes as typed, with no zone. */
  readonly temporalType = input<UiTemporalType | null>(null);
  /** The accessible name of the control. */
  readonly label = input('');
  /** What the control holds: a number, a boolean, the wire string of a date, or text; `null` for nothing. */
  readonly value = model<unknown>(null);

  protected readonly control = computed(() => CONTROLS[this.type()] ?? 'text');
  /** What the temporal control edits: a time of day or a date and time as the column's type says, a date otherwise. */
  protected readonly temporalKind = computed<TemporalKind>(() => {
    const type = this.type();
    return type === 'datetime' || type === 'time' ? type : 'date';
  });
  protected readonly numberValue = computed(() => {
    const value = this.value();
    return typeof value === 'number' ? value : null;
  });
  protected readonly textValue = computed(() => {
    const value = this.value();
    return value === null || value === undefined ? null : String(value);
  });

  /** A checkbox's values: any, yes and no. */
  protected readonly flags = computed<FixedSelect<boolean | null>>(() =>
    fixedSelectOf(this.label(), [
      [null, this.texts.of('Any')],
      [true, this.texts.of('Yes')],
      [false, this.texts.of('No')],
    ]),
  );
  protected readonly flagPicked = computed(() => {
    const value = this.value();
    return pickOf(this.flags(), typeof value === 'boolean' ? value : null);
  });
  protected readonly valueOfPick = valueOfPick;
}
