import { computed, linkedSignal } from '@angular/core';
import type { FilterGroup, FilterNode, FilterRule, GridModel } from '../../core/generated/contracts';
import { pickedValueOf, type SelectOption } from '../../select/select-option';
import { takesNoValue } from './grid-filter-operators';

/** A filter in the words of the list request: a rule, or a group of rules and groups joined by its one logic. */
export type GridFilter = FilterNode;

/** The rules of a filter at every depth, in order: itself, or the rules of its group's items. */
export function rulesOf(filter: GridFilter): readonly FilterRule[] {
  return filter.kind === 'FilterGroup' ? filter.items.flatMap(rulesOf) : [filter];
}

/**
 * The filters an AND joins in a filter, which its chips list one each: an AND group's items, the filter itself
 * otherwise — a rule, an OR group whole; none for no filter.
 */
export function conjunctsOf(filter: GridFilter | null | undefined): readonly GridFilter[] {
  if (!filter) {
    return [];
  }
  return filter.kind === 'FilterGroup' && filter.logic === 'and' ? filter.items : [filter];
}

/** The field a filter is kept under: the field of its first rule, however deep. */
export function fieldOf(filter: GridFilter): string {
  return rulesOf(filter)[0]?.field ?? '';
}

/** Whether a general filter is a pick: one rule that carries what a select picked; `picked` absent, null or empty is none. */
export function isPick(filter: GridFilter): boolean {
  return filter.kind === 'FilterRule' && !!filter.picked?.length;
}

/** The general filters a model opens with: what its initial filter's AND joins (`conjunctsOf`); none for a model without one. */
export function initialFiltersOf(model: Pick<GridModel, 'initialFilter'> | null | undefined): readonly GridFilter[] {
  return conjunctsOf(model?.initialFilter);
}

/** The filters of a set by their field (`fieldOf`), a later one of a field in place of the earlier; one without a field is left out. */
function byFieldOf(filters: readonly GridFilter[]): Readonly<Record<string, GridFilter>> {
  return Object.fromEntries(filters.filter(f => fieldOf(f)).map(f => [fieldOf(f), f]));
}

/**
 * A set of filters keyed by field: one filter per field, in the order they were added. The
 * grid keeps two, the reference columns' picks and the filters the columns hold; both travel with every
 * request, combined by AND on the server. It starts again whenever `resetBy` changes: from what `initial` reads of
 * the new value, empty without one.
 */
export class GridFilterSet<T = unknown> {
  private readonly byField = linkedSignal<T, Readonly<Record<string, GridFilter>>>({
    source: () => this.resetBy(),
    computation: source => byFieldOf(this.initial(source)),
  });

  /** The filters as a list, in insertion order. */
  readonly state = computed<readonly GridFilter[]>(() => Object.values(this.byField()));

  constructor(
    private readonly resetBy: () => T = () => null as T,
    private readonly initial: (source: T) => readonly GridFilter[] = () => [],
  ) {}

  get(field: string): GridFilter | undefined {
    return this.byField()[field];
  }

  /** Adds or replaces the filter of its field; a filter without a field is ignored. */
  add(filter: GridFilter): void {
    const field = fieldOf(filter);
    if (field) {
      this.byField.update(current => ({ ...current, [field]: filter }));
    }
  }

  removeByField(field: string): void {
    this.byField.update(current => {
      if (!(field in current)) {
        return current;
      }
      const { [field]: _removed, ...rest } = current;
      return rest;
    });
  }

  clear(): void {
    this.byField.set({});
  }

  /** Replaces the whole set. */
  set(filters: readonly GridFilter[]): void {
    this.byField.set(byFieldOf(filters));
  }
}

/** A rule with nothing to compare filters nothing, unless its operator takes no value. */
export function isEmptyRule(rule: FilterRule): boolean {
  if (!rule.field) {
    return true;
  }
  if (takesNoValue(rule.operator)) {
    return false;
  }
  const value = rule.value;
  return value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0);
}

/** A filter whose every rule is empty filters nothing. */
export function isEmptyFilter(filter: GridFilter): boolean {
  return normalizedFilter(filter) === undefined;
}

/**
 * A filter in its normal form: empty rules and groups dropped, a group of one item as that item, a group of its
 * parent's logic merged into the parent; nothing when nothing is left. It means what the filter means, one pair of
 * parentheses per group that is left.
 */
export function normalizedFilter(filter: GridFilter): GridFilter | undefined {
  if (filter.kind === 'FilterRule') {
    return isEmptyRule(filter) ? undefined : filter;
  }
  const items = filter.items.flatMap(item => {
    const normal = normalizedFilter(item);
    if (!normal) {
      return [];
    }
    return normal.kind === 'FilterGroup' && normal.logic === filter.logic ? normal.items : [normal];
  });
  if (items.length <= 1) {
    return items[0];
  }
  const group: FilterGroup = { kind: 'FilterGroup', logic: filter.logic, items };
  return group;
}

/**
 * The one filter of a reference column, whoever picks it — the header, the cell's link, the Filters overlay: `in` on
 * the column's key with the picked ids as the value and their names beside them; none when nothing is picked.
 */
export function referenceFilterOf(field: string, options: readonly SelectOption[]): GridFilter | null {
  return options.length === 0
    ? null
    : { kind: 'FilterRule', field, operator: 'in', value: options.map(o => o.id), picked: options.map(pickedValueOf) };
}

/** A set as the request carries it: the filters joined by AND in their normal form (`normalizedFilter`); nothing when nothing is left. */
export function toFilterNode(filters: readonly GridFilter[]): FilterNode | undefined {
  return normalizedFilter({ kind: 'FilterGroup', logic: 'and', items: [...filters] });
}
