import { Component, computed, inject } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import type { FormElementDto, FormInfoModel, UiFieldType } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { formatCellDate, formatCellNumber, formatCellText, formatCellTime, type ValueClock } from '../../grid/cell/grid-cell-format';
import { FormElementBase } from './form-element-base';

/** What an info element shows for a value that is absent. */
const NO_VALUE = '—';

/**
 * How an info element writes a value of each field type, as the grid's cell of that type writes it; a number alone
 * reads the grouping of the element's model.
 */
const FORMATS = {
  text: value => formatCellText(value),
  number: (value, clock, _, isGrouped) => formatCellNumber(value, clock.locale, null, isGrouped),
  date: (value, clock) => formatCellDate(value, 'date', clock.locale, clock.zone),
  datetime: (value, clock) => formatCellDate(value, 'datetime', clock.locale, clock.zone),
  time: (value, clock) => formatCellTime(value, clock.locale),
  checkbox: (value, _, texts) => texts.of(value ? 'Yes' : 'No'),
} satisfies Record<UiFieldType, (value: unknown, clock: ValueClock, texts: TextsService, isGrouped?: boolean | null) => string>;

/**
 * A value the server owns, shown and never edited: the element's label, then its value as text written by its type as
 * the grid's cell writes it — a number, a date, a stamp and a time of day in the session's culture, a number grouped
 * unless its model switches the groups off, an instant on the user's clock, a flag as yes or no
 * — and `—` when there is none. The value is text, never markup; a save does not carry it.
 */
@Component({
  selector: 'ui-form-info',
  template: `
    <dl class="m-0 min-w-0">
      <dt class="block mb-1 text-sm font-medium text-muted truncate">{{ element().label }}</dt>
      <dd class="m-0 min-h-8 flex items-center text-sm text-ink break-words" [id]="id()" data-info-value>{{ text() }}</dd>
    </dl>
    @if (element().help; as help) {
      <p class="m-0 mt-1 text-xs text-muted">{{ help }}</p>
    }
  `,
  host: { class: 'block min-w-0' },
})
export class FormInfoComponent extends FormElementBase<unknown, FormInfoModel> {
  private readonly locale = inject(LocaleService);
  private readonly texts = inject(TextsService);

  protected readonly text = computed(() =>
    infoTextOf(
      this.form.valueOf(this.path()),
      this.element(),
      { locale: this.locale.current(), zone: this.locale.zone() },
      this.texts,
      this.model()?.isGrouped,
    ),
  );
}

/** The value as the element's type writes it, a number by `isGrouped`; the dash for nothing, an empty text included. */
function infoTextOf(
  value: unknown,
  element: FormElementDto,
  clock: ValueClock,
  texts: TextsService,
  isGrouped: boolean | null | undefined,
): string {
  if (value === null || value === undefined || value === '') {
    return NO_VALUE;
  }
  const format = isUiFieldType(element.type) ? FORMATS[element.type] : FORMATS.text;
  return format(value, clock, texts, isGrouped) || NO_VALUE;
}

function isUiFieldType(type: FormElementDto['type']): type is UiFieldType {
  return Object.hasOwn(FORMATS, type);
}
