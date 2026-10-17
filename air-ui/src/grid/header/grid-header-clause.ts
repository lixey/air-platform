import { inject } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import type { GridColumnDto } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import type { GridFilter } from '../filter/grid-filter-set';
import { clauseText, connectorOf } from '../filter/grid-filter-text';

/**
 * The writer of a column's filter as a header reads it: its clause in the session's culture, on the user's clock,
 * joined by the texts' connector. Called where `inject` is: a field of a header.
 */
export function injectClauseText(): (filter: GridFilter, column: GridColumnDto) => string {
  const connector = connectorOf(inject(TextsService));
  const { current, zone } = inject(LocaleService);
  return (filter, column) => clauseText(filter, column, connector, current(), zone());
}
