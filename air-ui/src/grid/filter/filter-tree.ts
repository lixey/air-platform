import type { FilterLogic, FilterNode, FilterOperator, FilterRule, GridColumnDto, PickedValue } from '../../core/generated/contracts';
import { startOperatorOf, takesNoValue } from './grid-filter-operators';
import { normalizedFilter } from './grid-filter-set';
import { filterValueOf } from './grid-filter-value';

/** A condition of the filter tree: a column, an operator and the value its control wrote. */
export interface TreeRule {
  readonly kind: 'rule';
  readonly id: number;
  readonly field: string;
  readonly operator: FilterOperator;
  readonly value: unknown;
  /** What a select picked, kept from the filter the tree opened on. */
  readonly picked?: readonly PickedValue[] | null;
}

/**
 * A group of the filter tree: one connector joining its items, one pair of parentheses of the query. The root holds
 * conditions and subgroups, a subgroup conditions only.
 */
export interface TreeGroup {
  readonly kind: 'group';
  readonly id: number;
  readonly logic: FilterLogic;
  readonly items: readonly TreeItem[];
}

export type TreeItem = TreeRule | TreeGroup;

/** The change of a condition: its column, operator or value. */
export type TreeRuleChange = Partial<Pick<TreeRule, 'field' | 'operator' | 'value'>>;

let lastId = 0;

/** A new condition over the column, from the operator its menu starts with and no value. */
export function newTreeRule(column: GridColumnDto | null | undefined): TreeRule {
  return { kind: 'rule', id: ++lastId, field: column?.path ?? '', operator: startOperatorOf(column), value: null };
}

/** A new group of the connector over the items. */
export function newTreeGroup(logic: FilterLogic, items: readonly TreeItem[] = []): TreeGroup {
  return { kind: 'group', id: ++lastId, logic, items };
}

/**
 * A filter as the tree draws it: its normal form (`normalizedFilter`) as the root group — a rule as the root's one
 * condition, a group's rules and groups of rules as the root's items; an empty root for no filter, and `null` for a
 * filter that nests deeper than the root and one level of subgroups.
 */
export function treeOf(filter: FilterNode | null | undefined): TreeGroup | null {
  const normal = filter ? normalizedFilter(filter) : undefined;
  if (!normal) {
    return newTreeGroup('and');
  }
  if (normal.kind === 'FilterRule') {
    return newTreeGroup('and', [treeRuleOf(normal)]);
  }
  const items: TreeItem[] = [];
  for (const item of normal.items) {
    if (item.kind === 'FilterRule') {
      items.push(treeRuleOf(item));
    } else if (item.items.every(child => child.kind === 'FilterRule')) {
      items.push(
        newTreeGroup(
          item.logic,
          item.items.map(child => treeRuleOf(child as FilterRule)),
        ),
      );
    } else {
      return null;
    }
  }
  return newTreeGroup(normal.logic, items);
}

/**
 * What a tree posts: its groups and rules in the words of the list request, each value as its column's control posts
 * it (`filterValueOf`), in normal form — no empty rule or group, a group of one item as that item, a subgroup of its
 * parent's connector merged into the parent; nothing when nothing is left.
 */
export function nodeOf(tree: TreeGroup, columns: readonly GridColumnDto[] = []): FilterNode | undefined {
  return normalizedFilter(wireOf(tree, columns));
}

/** The conditions of a tree at every depth, in order. */
export function treeRulesOf(item: TreeItem): readonly TreeRule[] {
  return item.kind === 'rule' ? [item] : item.items.flatMap(treeRulesOf);
}

/** The tree with the item appended to the group of the id. */
export function addTreeItem(tree: TreeGroup, groupId: number, item: TreeItem): TreeGroup {
  return mapGroups(tree, group => (group.id === groupId ? { ...group, items: [...group.items, item] } : group));
}

/** The tree without the item of the id, wherever it stands. */
export function removeTreeItem(tree: TreeGroup, id: number): TreeGroup {
  return mapGroups(tree, group =>
    group.items.some(item => item.id === id) ? { ...group, items: group.items.filter(item => item.id !== id) } : group,
  );
}

/** The tree with the condition of the id changed. */
export function patchTreeRule(tree: TreeGroup, id: number, change: TreeRuleChange): TreeGroup {
  return mapGroups(tree, group =>
    group.items.some(item => item.id === id && item.kind === 'rule')
      ? { ...group, items: group.items.map(item => (item.id === id && item.kind === 'rule' ? { ...item, ...change } : item)) }
      : group,
  );
}

/** The tree with the group of the id joined by the connector. */
export function setTreeLogic(tree: TreeGroup, groupId: number, logic: FilterLogic): TreeGroup {
  return mapGroups(tree, group => (group.id === groupId ? { ...group, logic } : group));
}

/** The group rebuilt bottom up, each group passed through `change` after its subgroups. */
function mapGroups(group: TreeGroup, change: (group: TreeGroup) => TreeGroup): TreeGroup {
  const items = group.items.map(item => (item.kind === 'group' ? mapGroups(item, change) : item));
  const changed = items.some((item, index) => item !== group.items[index]);
  return change(changed ? { ...group, items } : group);
}

function treeRuleOf(rule: FilterRule): TreeRule {
  return {
    kind: 'rule',
    id: ++lastId,
    field: rule.field,
    operator: rule.operator,
    value: rule.value ?? null,
    ...(rule.picked?.length ? { picked: rule.picked } : {}),
  };
}

function wireOf(item: TreeItem, columns: readonly GridColumnDto[]): FilterNode {
  if (item.kind === 'group') {
    return { kind: 'FilterGroup', logic: item.logic, items: item.items.map(child => wireOf(child, columns)) };
  }
  const rule: FilterRule = { kind: 'FilterRule', field: item.field, operator: item.operator };
  if (!takesNoValue(item.operator)) {
    const column = columns.find(c => c.path === item.field);
    // A list is a select's pick, which no control of the tree writes: it travels as it came
    rule.value = column && !Array.isArray(item.value) ? filterValueOf(column.type, item.value) : item.value;
  }
  if (item.picked?.length) {
    rule.picked = [...item.picked];
  }
  return rule;
}
