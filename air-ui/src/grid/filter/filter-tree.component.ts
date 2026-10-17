import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  booleanAttribute,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  untracked,
} from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import { BtnComponent } from '../../btn/btn.component';
import type { FilterLogic, FilterNode, FilterOperator, GridColumnDto, UiFieldType, UiTemporalType } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import type { SelectOption } from '../../select/select-option';
import { SelectComponent } from '../../select/select.component';
import { columnLabelOf } from '../column/grid-column-label';
import { temporalTypeOf } from '../column/grid-column-temporal-type';
import {
  addTreeItem,
  newTreeGroup,
  newTreeRule,
  nodeOf,
  patchTreeRule,
  removeTreeItem,
  setTreeLogic,
  treeOf,
  treeRulesOf,
  type TreeGroup,
  type TreeItem,
  type TreeRule,
  type TreeRuleChange,
} from './filter-tree';
import { fixedSelectOf, pickOf, valueOfPick, type FixedSelect } from './fixed-select';
import { menuOperators, takesNoValue } from './grid-filter-operators';
import { normalizedFilter } from './grid-filter-set';
import { chipLabel, clauseText, connectorOf } from './grid-filter-text';
import { GridFilterValueComponent } from './grid-filter-value.component';

/** The prefix cell of an item, fixed in width: the text `Where`, the connector's select or its word. */
const PREFIX = 'flex-none w-24 min-h-9 flex items-center';

/** The word of a prefix cell. */
const PREFIX_WORD = 'px-2 text-sm text-muted';

/** A select no column offers operators for: drawn only while `fields` is empty, which draws no condition. */
const NO_OPERATORS = fixedSelectOf<FilterOperator>('', []);

/**
 * A filter as a tree of groups: the root joins its conditions and subgroups by one connector, a subgroup joins its
 * conditions by its own, each group one pair of parentheses of the query. Each item is one row: a prefix cell of a fixed
 * width — `Where` when it is the first of its group, the choice of `FilterAnd` or `FilterOr` when it is the second (the
 * choice sets the group's connector, the select named `ConditionsJoin`), the connector's word after that — then the
 * item, taking the rest of the row. A condition is a row of a column (unless the tree is `fixed` to one), an operator of
 * the column's `operators`, the value typed in the library's control for the column's type and a control that removes
 * it; a condition keeps one line while its value has 128 px beside the column and the operator, else its value and the
 * control that removes it wrap to a second line, so the tree never scrolls sideways; a subgroup is a box on
 * `surface-1`, its heading `AllAreTrue` or `AnyIsTrue` with controls that add a condition to it and remove it, its
 * conditions stacked under the heading, and offers no subgroup. The column, the operator and the
 * connector are the library's select over a fixed list. Under the root: `AddCondition` and `AddConditionGroup` at the
 * start, `ClearAll` at the end. A filter deeper than the tree draws, or over a column `fields` does not offer, is shown
 * as its formula, read-only, with `ClearAll`.
 *
 * The tree is a draft of `filter`, read again only when `filter` changes; its host applies `node()`. A new condition's
 * operator takes the focus; `Enter` in a value says `applied`, `ClearAll` says `cleared`.
 */
@Component({
  selector: 'ui-filter-tree',
  imports: [BtnComponent, GridFilterValueComponent, NgTemplateOutlet, SelectComponent],
  template: `
    <ng-template #items let-group>
      @for (item of group.items; track item.id; let index = $index) {
        <div class="flex w-full min-w-0 items-start gap-2" data-filter-item>
          <div class="${PREFIX}">
            @if (index === 0) {
              <span class="${PREFIX_WORD}">{{ texts.of('Where') }}</span>
            } @else if (index === 1) {
              <ui-select
                [model]="connectors().source"
                [clearable]="false"
                [picked]="pickOf(connectors(), group.logic)"
                (pickedChange)="setLogic(group, $event)"
              />
            } @else {
              <span class="${PREFIX_WORD}">{{ connector(group.logic) }}</span>
            }
          </div>

          @if (item.kind === 'rule') {
            @let operators = operatorsOf(item);
            <div class="flex flex-1 min-w-0 flex-wrap items-center gap-2" data-filter-condition>
              @if (picksColumn()) {
                <div class="flex-none w-40 min-w-0">
                  <ui-select
                    [model]="columns().source"
                    [clearable]="false"
                    [picked]="pickOf(columns(), item.field)"
                    (pickedChange)="setField(item, $event)"
                  />
                </div>
              }
              <div class="flex-none w-44 min-w-0">
                <ui-select
                  [id]="operatorId(item)"
                  [model]="operators.source"
                  [clearable]="false"
                  [picked]="pickOf(operators, item.operator)"
                  (pickedChange)="setOperator(item, operators, $event)"
                />
              </div>
              <div class="flex flex-1 basis-32 min-w-32">
                @if (!takesNoValue(item.operator)) {
                  <ui-grid-filter-value
                    [type]="typeOf(item)"
                    [temporalType]="temporalTypeOfRule(item)"
                    [label]="texts.of('ValueFor', labelOf(item))"
                    [value]="item.value"
                    (valueChange)="patch(item, { value: $event })"
                    (keydown.enter)="applied.emit()"
                  />
                }
              </div>
              <ui-btn
                icon="trash"
                variant="ghost"
                color="slate"
                class="flex-none"
                [title]="texts.of('RemoveCondition')"
                (click)="remove(item)"
              />
            </div>
          } @else {
            <div
              role="group"
              class="flex flex-1 min-w-0 flex-col gap-2 p-3 rounded-lg border border-line bg-surface-1"
              [attr.aria-labelledby]="headingId(item)"
            >
              <div class="flex w-full min-w-0 items-center gap-1">
                <span class="flex-1 min-w-0 truncate px-2 text-sm text-ink" [id]="headingId(item)">{{ headingOf(item) }}</span>
                <ui-btn
                  icon="plus"
                  variant="ghost"
                  color="slate"
                  class="flex-none"
                  [title]="texts.of('AddCondition')"
                  (click)="addRule(item)"
                />
                <ui-btn
                  icon="trash"
                  variant="ghost"
                  color="slate"
                  class="flex-none"
                  [title]="texts.of('RemoveGroup')"
                  (click)="remove(item)"
                />
              </div>
              <ng-container [ngTemplateOutlet]="items" [ngTemplateOutletContext]="{ $implicit: item }" />
            </div>
          }
        </div>
      }
    </ng-template>

    @if (tree(); as root) {
      <div role="group" class="flex flex-col gap-2 min-w-0" [attr.aria-label]="headingOf(root)">
        <ng-container [ngTemplateOutlet]="items" [ngTemplateOutletContext]="{ $implicit: root }" />
        @if (root.items.length === 0) {
          <p class="m-0 text-sm text-muted">{{ texts.of('NoFilterYet') }}</p>
        }
      </div>
      <div class="flex w-full min-w-0 items-center justify-between gap-2">
        <div class="flex min-w-0 flex-wrap items-center gap-2">
          <ui-btn
            icon="plus"
            variant="ghost"
            color="slate"
            [label]="texts.of('AddCondition')"
            [disabled]="fields().length === 0"
            (click)="addRule(root)"
          />
          <ui-btn
            icon="plus"
            variant="ghost"
            color="slate"
            [label]="texts.of('AddConditionGroup')"
            [disabled]="fields().length === 0"
            (click)="addGroup(root)"
          />
        </div>
        <ui-btn icon="trash" variant="ghost" color="red" class="flex-none" [label]="texts.of('ClearAll')" (click)="cleared.emit()" />
      </div>
    } @else {
      <p class="m-0 text-sm text-ink break-words">{{ formula() }}</p>
      <div class="flex justify-end">
        <ui-btn icon="trash" variant="ghost" color="red" [label]="texts.of('ClearAll')" (click)="cleared.emit()" />
      </div>
    }
  `,
  host: { class: 'flex flex-col gap-3 min-w-0' },
})
export class FilterTreeComponent {
  private static next = 0;

  protected readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService).current;
  private readonly zone = inject(LocaleService).zone;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);
  /** The word that joins two clauses of a group, in the session's culture. */
  protected readonly connector = connectorOf(this.texts);
  /** The stem of the ids the tree gives its operators and headings. */
  private readonly id = `ui-filter-tree-${++FilterTreeComponent.next}`;

  /** The filter the tree opens on; a new one starts the draft again. */
  readonly filter = input<FilterNode | undefined>();
  /** The columns a condition may filter. */
  readonly fields = input.required<readonly GridColumnDto[]>();
  /** Whether every condition filters the first of `fields`, so no condition draws a column select. */
  readonly fixed = input(false, { transform: booleanAttribute });
  /** Whether an empty filter opens with one condition over the first column. */
  readonly seeded = input(false, { transform: booleanAttribute });

  /** `Enter` in a value: the host applies. */
  readonly applied = output<void>();
  /** `ClearAll`: the host removes the filter. */
  readonly cleared = output<void>();

  /** The draft: `filter` as a tree, or `null` while it is shown read-only. */
  protected readonly tree = linkedSignal<TreeGroup | null>(() => {
    const filter = this.filter();
    return untracked(() => this.draftOf(filter));
  });

  /** What the tree posts now, in normal form: the draft's, or the opening filter's while it is read-only; none when empty. */
  readonly node = computed<FilterNode | undefined>(() => {
    const tree = this.tree();
    if (tree) {
      return nodeOf(tree, this.fields());
    }
    const filter = this.filter();
    return filter ? normalizedFilter(filter) : undefined;
  });

  protected readonly picksColumn = computed(() => !this.fixed());

  /** The read-only filter's formula: the header's clauses for a fixed column, the chip's labelled ones otherwise. */
  protected readonly formula = computed(() => {
    const filter = this.filter();
    if (!filter) {
      return '';
    }
    const fields = this.fields();
    return this.fixed()
      ? clauseText(filter, fields[0], this.connector, this.locale(), this.zone())
      : chipLabel(filter, fields, this.connector, this.locale(), this.zone());
  });

  /** The connector's select: AND or OR, named `ConditionsJoin`. */
  protected readonly connectors = computed(() =>
    fixedSelectOf<FilterLogic>(this.texts.of('ConditionsJoin'), [
      ['and', this.texts.of('FilterAnd')],
      ['or', this.texts.of('FilterOr')],
    ]),
  );

  /** The column's select: the columns `fields` offers. */
  protected readonly columns = computed(() =>
    fixedSelectOf<string>(
      this.texts.of('Column'),
      this.fields().map(column => [column.path ?? '', columnLabelOf(column)] as const),
    ),
  );

  /** The operator's select of each column, by its path: the operators its menu offers, named `OperatorFor` the column. */
  private readonly operatorSelects = computed(
    () =>
      new Map(
        this.fields().map(column => [
          column.path ?? '',
          fixedSelectOf<FilterOperator>(
            this.texts.of('OperatorFor', columnLabelOf(column)),
            menuOperators(column).map(operator => [operator.handle, operator.label] as const),
          ),
        ]),
      ),
  );

  protected readonly takesNoValue = takesNoValue;
  protected readonly pickOf = pickOf;

  /** Empties the draft, a read-only filter's formula too, so `node()` answers none until the host applies it. */
  clear(): void {
    this.tree.set(this.draftOf(undefined));
  }

  protected headingOf(group: TreeGroup): string {
    return this.texts.of(group.logic === 'or' ? 'AnyIsTrue' : 'AllAreTrue');
  }

  protected headingId(group: TreeGroup): string {
    return `${this.id}-group-${group.id}`;
  }

  protected operatorId(rule: TreeRule): string {
    return `${this.id}-operator-${rule.id}`;
  }

  protected operatorsOf(rule: TreeRule): FixedSelect<FilterOperator> {
    return this.operatorSelects().get(rule.field) ?? NO_OPERATORS;
  }

  protected typeOf(rule: TreeRule): UiFieldType {
    return this.columnOf(rule)?.type ?? 'text';
  }

  /** How the rule's column writes a date: `temporalTypeOf(column)`, the date range menu's rule; `null` for no column. */
  protected temporalTypeOfRule(rule: TreeRule): UiTemporalType | null {
    const column = this.columnOf(rule);
    return column ? temporalTypeOf(column) : null;
  }

  protected labelOf(rule: TreeRule): string {
    const column = this.columnOf(rule);
    return column ? columnLabelOf(column) : rule.field;
  }

  protected addRule(group: TreeGroup): void {
    const rule = newTreeRule(this.fields()[0]);
    this.update(tree => addTreeItem(tree, group.id, rule));
    this.focusOperator(rule);
  }

  protected addGroup(root: TreeGroup): void {
    const rule = newTreeRule(this.fields()[0]);
    this.update(tree => addTreeItem(tree, root.id, newTreeGroup('and', [rule])));
    this.focusOperator(rule);
  }

  protected remove(item: TreeItem): void {
    this.update(tree => removeTreeItem(tree, item.id));
  }

  protected setLogic(group: TreeGroup, picked: readonly SelectOption[]): void {
    const logic = valueOfPick(this.connectors(), picked);
    if (logic) {
      this.update(tree => setTreeLogic(tree, group.id, logic));
    }
  }

  protected setField(rule: TreeRule, picked: readonly SelectOption[]): void {
    const field = valueOfPick(this.columns(), picked);
    if (field !== undefined && field !== rule.field) {
      const start = newTreeRule(this.fields().find(c => c.path === field));
      this.patch(rule, { field: start.field, operator: start.operator, value: null });
    }
  }

  protected setOperator(rule: TreeRule, operators: FixedSelect<FilterOperator>, picked: readonly SelectOption[]): void {
    const operator = valueOfPick(operators, picked);
    if (operator) {
      this.patch(rule, { operator });
    }
  }

  protected patch(rule: TreeRule, change: TreeRuleChange): void {
    this.update(tree => patchTreeRule(tree, rule.id, change));
  }

  private update(change: (tree: TreeGroup) => TreeGroup): void {
    this.tree.update(tree => (tree ? change(tree) : tree));
  }

  private columnOf(rule: TreeRule): GridColumnDto | undefined {
    return this.fields().find(column => column.path === rule.field);
  }

  /** The draft of a filter: its tree, seeded when asked; `null` when it nests too deep or filters a column not offered. */
  private draftOf(filter: FilterNode | undefined): TreeGroup | null {
    const tree = treeOf(filter);
    const fields = this.fields();
    if (!tree || !treeRulesOf(tree).every(rule => fields.some(column => column.path === rule.field))) {
      return null;
    }
    return this.seeded() && tree.items.length === 0 ? addTreeItem(tree, tree.id, newTreeRule(fields[0])) : tree;
  }

  private focusOperator(rule: TreeRule): void {
    afterNextRender(() => this.host.querySelector<HTMLElement>(`#${this.operatorId(rule)} [role="combobox"]`)?.focus(), {
      injector: this.injector,
    });
  }
}
