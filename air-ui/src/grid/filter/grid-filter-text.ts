import type { FilterRule, GridColumnDto } from '../../core/generated/contracts';
import type { TextsService } from '../../core/texts.service';
import { browserZone } from '../../core/time-zone';
import { formatCellValue, type ValueClock } from '../cell/grid-cell-format';
import { columnLabelOf } from '../column/grid-column-label';
import { operatorOf, takesNoValue } from './grid-filter-operators';
import { type GridFilter, rulesOf } from './grid-filter-set';

/** The word that joins two clauses of a group, by its logic: the platform's `FilterAnd` or `FilterOr` in the request's culture. */
export type Connector = (logic: 'and' | 'or') => string;

/** The platform's connector in the session's culture: `FilterOr` for `or`, `FilterAnd` for `and`. */
export function connectorOf(texts: Pick<TextsService, 'of'>): Connector {
  return logic => texts.of(logic === 'or' ? 'FilterOr' : 'FilterAnd');
}

/**
 * What a chip in the nav says: `Sort Order >= 10`, each further item after its group's connector, a group in
 * parentheses; a value is written as its column's cell writes it in `locale`, an instant on the clock of `zone`.
 */
export function chipLabel(
  filter: GridFilter,
  columns: readonly GridColumnDto[],
  connector: Connector = logic => logic.toUpperCase(),
  locale = 'en-US',
  zone = browserZone(),
): string {
  const first = rulesOf(filter)[0];
  const picked = first?.picked;
  if (picked?.length) {
    // What a select picked: the names, not the ids
    const column = columnOf(columns, first.field);
    const label = labelOf(column, first.field);
    const names = picked.map(v => v.label).join(', ');
    return first.operator === 'in' ? `${label}: ${names}` : `${label} ${symbolOf(first, column)} ${names}`;
  }

  return formula(filter, true, connector, { locale, zone }, rule => {
    const column = columnOf(columns, rule.field);
    return { column, label: labelOf(column, rule.field) };
  });
}

/**
 * What a header shows of a filter the menu made: the operator's symbol and the value, `>= 10`, each further item after
 * its group's connector in lower case, a group below the first in parentheses; a value is written as its column's
 * cell writes it in `locale`, an instant on the clock of `zone`.
 */
export function clauseText(
  filter: GridFilter,
  column?: GridColumnDto | null,
  connector: Connector = logic => logic,
  locale = 'en-US',
  zone = browserZone(),
): string {
  return formula(
    filter,
    false,
    logic => connector(logic).toLocaleLowerCase(),
    { locale, zone },
    () => ({ column, label: '' }),
  );
}

/** The column a rule's clause is written by, and the label it starts with. */
type ClauseColumn = (rule: FilterRule) => { column: GridColumnDto | null | undefined; label: string };

/** A filter's formula: its rules' clauses joined by their groups' connectors, every group but an outer one bracketed. */
function formula(filter: GridFilter, brackets: boolean, connector: Connector, clock: ValueClock, columnOfRule: ClauseColumn): string {
  if (filter.kind === 'FilterRule') {
    const { column, label } = columnOfRule(filter);
    return clause(label, filter, column, clock);
  }
  const text = filter.items
    .map((item, index) => {
      const part = formula(item, true, connector, clock, columnOfRule);
      return index === 0 ? part : `${connector(filter.logic)} ${part}`;
    })
    .join(' ');
  return brackets ? `(${text})` : text;
}

function columnOf(columns: readonly GridColumnDto[], field: string): GridColumnDto | undefined {
  return columns.find(c => c.path === field || c.filterPath === field);
}

function labelOf(column: GridColumnDto | undefined, field: string): string {
  return column ? columnLabelOf(column) : field;
}

function clause(label: string, rule: FilterRule, column: GridColumnDto | null | undefined, clock: ValueClock): string {
  const value = takesNoValue(rule.operator) ? '' : textOf(rule.value, column, clock);
  return `${label} ${symbolOf(rule, column)} ${value}`.trim();
}

/** The operator as the server shows it: its symbol, or its label when it has none. */
function symbolOf(rule: FilterRule, column: GridColumnDto | null | undefined): string {
  const operator = operatorOf(column, rule.operator);
  return operator ? (operator.symbol ?? operator.label.toLowerCase()) : rule.operator;
}

/** A value as the column's cell writes it, a list's values joined by commas. */
function textOf(value: unknown, column: GridColumnDto | null | undefined, clock: ValueClock): string {
  return Array.isArray(value)
    ? value.map(v => textOf(v, column, clock)).join(', ')
    : formatCellValue(value, column, clock.locale, clock.zone);
}
