import type { FilterOperator, GridColumnDto, GridFilterOperatorDto, UiFieldType } from '../../core/generated/contracts';

/** Operators that compare with nothing: no value control is drawn for them. */
export function takesNoValue(operator: FilterOperator | null | undefined): boolean {
  return operator === 'isNull' || operator === 'isNotNull';
}

/** Operators whose value is a list: only the select of a reference column can give one. */
export function takesList(operator: FilterOperator | null | undefined): boolean {
  return operator === 'in' || operator === 'notIn';
}

/** What a column's operator menu offers: the operators the server sent, without those whose value it cannot draw. */
export function menuOperators(column: GridColumnDto | null | undefined): readonly GridFilterOperatorDto[] {
  return (column?.operators ?? []).filter(o => !takesList(o.handle));
}

/** The operator with this word among those the server sent for the column. */
export function operatorOf(column: GridColumnDto | null | undefined, handle: FilterOperator): GridFilterOperatorDto | undefined {
  return column?.operators?.find(o => o.handle === handle);
}

/** The operator a column's filter starts with: pattern matching for text, equality for the rest. */
export function defaultOperatorFor(type: UiFieldType): FilterOperator {
  return type === 'text' ? 'matches' : 'equals';
}

/**
 * The operator a clause of a column's menu starts with: the one the
 * header's box writes when the server sent it, the first sent otherwise.
 */
export function startOperatorOf(column: GridColumnDto | null | undefined): FilterOperator {
  const offered = menuOperators(column);
  const preferred = defaultOperatorFor(column?.type ?? 'text');
  return offered.some(o => o.handle === preferred) ? preferred : (offered[0]?.handle ?? preferred);
}
