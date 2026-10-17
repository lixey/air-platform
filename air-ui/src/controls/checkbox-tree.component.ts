import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, model, signal } from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';
import type { CheckboxTreeNodeDto } from '../core/generated/contracts';
import { CHECKBOX_CLASSES, FIELD_FOCUS_WITHIN, INVALID_EDGE } from '../theme/css-classes';
import { FIELD_DESCRIPTION } from './field-description';
import { UiControlDirective, type UiControlState } from './ui-control.directive';

/**
 * How the value draws a node: checked when it is held or lies under a held node, which fixes it; indeterminate when
 * it is neither while a node under it is held.
 */
interface CheckboxTreeNodeState {
  readonly isChecked: boolean;
  readonly isCovered: boolean;
  readonly isIndeterminate: boolean;
}

/** A node as the tree draws it: the node, the state its box reads, its children, and how its line and theirs are laid out. */
interface CheckboxTreeRow {
  readonly node: CheckboxTreeNodeDto;
  readonly box: UiControlState;
  readonly children: readonly CheckboxTreeRow[];
  /** The band and the label's weight of the node's own line. */
  readonly band: string;
  /** The indent class of the node's line, and of the line its leaves share. */
  readonly indent: string;
  readonly leafIndent: string;
  /** The space above the node's item: a block that follows a sibling stands apart. */
  readonly space: string;
  /** Whether the children are all leaves, drawn on one line under the node. */
  readonly isLeafLine: boolean;
}

const UNHELD: CheckboxTreeNodeState = { isChecked: false, isCovered: false, isIndeterminate: false };

/**
 * The indent of a line by its depth: the root's box as far from the border as from the band's top (4px), and each level
 * the box and its gap further (32px), so a child's box stands under its parent's text. A deeper line takes the last.
 */
const INDENTS = ['pl-1', 'pl-9', 'pl-17', 'pl-25', 'pl-33', 'pl-41'] as const;

const LINE = 'flex items-center min-h-8 pr-3';
const LABEL = 'flex flex-1 items-center gap-2 min-w-0 min-h-8 text-base select-none';

/**
 * A tree of checkboxes editing the keys of the top-most checked nodes, implementing FormValueControl. A checked node
 * stands for its subtree: the nodes under it are drawn checked and take no toggle, and checking it takes their keys
 * out of the value. A node with a checked node under it is drawn indeterminate, and stays so when every child is
 * checked, because a node stands for more than the children it has today. A disabled node keeps its state.
 *
 * Layout: every line spans the tree on a band of its level, indented by its depth. A node with children draws on a band
 * lighter with each level: the root's semibold, the second level's semibold under a line, deeper ones medium; leaves
 * draw on the surface. A node with children that follows a sibling stands apart by a space of the surface. The leaves of a
 * node whose children are all leaves share one wrapping line under it; other children stand one per line. A node's
 * note is drawn muted at the end of its line, outside its label, so it can be selected without toggling the box. A
 * checked box that takes no toggle is drawn pale, so the boxes the user checked stand out from the ones they stand for.
 */
@Component({
  selector: 'ui-checkbox-tree',
  imports: [UiControlDirective, NgTemplateOutlet],
  template: `
    <ng-template #line let-row let-indent="indent">
      @let state = states().get(row.node.key) ?? unheld;
      <div [class]="'${LINE} ' + row.band + ' ' + indent">
        <label class="${LABEL}" [class.cursor-pointer]="!row.box.disabled() && !readonly()" [class.text-muted]="row.box.disabled()">
          <input
            type="checkbox"
            class="${CHECKBOX_CLASSES}"
            [uiControl]="row.box"
            [uiControlValue]="state.isChecked"
            [indeterminate]="state.isIndeterminate"
            (change)="toggle(row.node, $event)"
          />
          <span>{{ row.node.label }}</span>
        </label>
        @if (row.node.note) {
          <span class="ml-auto shrink-0 pl-3 text-sm font-normal text-muted select-text" [id]="noteId(row.node)">{{ row.node.note }}</span>
        }
      </div>
    </ng-template>
    <ng-template #branch let-rows>
      @for (row of rows; track row.node.key) {
        <li [class]="row.space">
          <ng-container [ngTemplateOutlet]="line" [ngTemplateOutletContext]="{ $implicit: row, indent: row.indent }" />
          @if (row.isLeafLine) {
            <ul [class]="'flex flex-wrap gap-x-5 pr-3 bg-surface ' + row.leafIndent" role="list">
              @for (leaf of row.children; track leaf.node.key) {
                <li>
                  <ng-container [ngTemplateOutlet]="line" [ngTemplateOutletContext]="{ $implicit: leaf, indent: '' }" />
                </li>
              }
            </ul>
          } @else if (row.children.length > 0) {
            <ul role="list">
              <ng-container [ngTemplateOutlet]="branch" [ngTemplateOutletContext]="{ $implicit: row.children }" />
            </ul>
          }
        </li>
      }
    </ng-template>
    <div
      [class]="
        'min-h-9 overflow-hidden rounded-lg border bg-surface text-ink ${FIELD_FOCUS_WITHIN} ' +
        (invalid() ? '${INVALID_EDGE}' : 'border-field-edge')
      "
      role="group"
      [attr.aria-label]="label()"
      [attr.aria-describedby]="description?.() ?? null"
    >
      @if (rows().length > 0) {
        <ul role="list">
          <ng-container [ngTemplateOutlet]="branch" [ngTemplateOutletContext]="{ $implicit: rows() }" />
        </ul>
      } @else {
        <span class="block p-2 text-base text-muted">—</span>
      }
    </div>
  `,
})
export class CheckboxTreeComponent implements FormValueControl<readonly string[] | null> {
  private static next = 0;

  readonly value = model<readonly string[] | null>(null);

  readonly nodes = input<readonly CheckboxTreeNodeDto[]>([]);
  readonly disabled = input(false);
  readonly readonly = input(false);
  readonly required = input(false);
  readonly invalid = input(false);
  readonly label = input<string | null>(null);

  protected readonly unheld = UNHELD;

  /** What describes the tree: its group carries it; a node is described by its note. */
  protected readonly description = inject(FIELD_DESCRIPTION, { optional: true });
  private readonly id = `ui-checkbox-tree-${++CheckboxTreeComponent.next}`;

  protected readonly states = computed(() => statesOf(this.nodes(), new Set(this.value() ?? [])));

  /** One box per node, built when the nodes change: no id and no native required, which would ask for every box to be checked. */
  protected readonly rows = computed(() => this.nodes().map((node, index) => this.rowOf(node, 0, index === 0)));

  /** Checking a node holds it alone for its subtree; unchecking it leaves the subtree unchecked. */
  protected toggle(node: CheckboxTreeNodeDto, event: Event): void {
    const on = (event.target as HTMLInputElement).checked;
    const subtree = new Set(keysOf(node));
    const next = new Set((this.value() ?? []).filter(key => !subtree.has(key)));
    if (on) {
      next.add(node.key);
    }
    this.value.set([...next]);
  }

  /** The id of a node's note, which describes its box. */
  protected noteId(node: CheckboxTreeNodeDto): string {
    return `${this.id}-note-${node.key}`;
  }

  private rowOf(node: CheckboxTreeNodeDto, depth: number, isFirst: boolean): CheckboxTreeRow {
    const isFixed = computed(() => node.isDisabled || (this.states().get(node.key)?.isCovered ?? false));
    return {
      node,
      box: {
        disabled: computed(() => this.disabled() || isFixed()),
        readonly: this.readonly,
        invalid: this.invalid,
        describedBy: signal(node.note ? this.noteId(node) : null),
      },
      children: node.children.map((child, index) => this.rowOf(child, depth + 1, index === 0)),
      band: bandOf(depth, node.children.length > 0),
      indent: indentOf(depth),
      leafIndent: indentOf(depth + 1),
      space: spaceOf(depth, node.children.length > 0, isFirst),
      isLeafLine: node.children.length > 0 && node.children.every(child => child.children.length === 0),
    };
  }
}

/** The space above a node with children that follows a sibling: wider near the root. */
function spaceOf(depth: number, hasChildren: boolean, isFirst: boolean): string {
  if (!hasChildren || isFirst) {
    return '';
  }
  return depth < 2 ? 'mt-2' : 'mt-1';
}

/** The band and the label's weight of a line by its depth: a node with children lighter with each level, a leaf on the surface. */
function bandOf(depth: number, hasChildren: boolean): string {
  if (!hasChildren) {
    return 'bg-surface';
  }

  if (depth === 0) {
    return 'bg-surface-3 font-semibold';
  }

  return depth === 1 ? 'bg-surface-2 font-semibold border-t border-line' : 'bg-row-stripe font-medium';
}

function indentOf(depth: number): string {
  return INDENTS[Math.min(depth, INDENTS.length - 1)];
}

/** The state of every node under the held keys, by key. */
function statesOf(nodes: readonly CheckboxTreeNodeDto[], held: ReadonlySet<string>): ReadonlyMap<string, CheckboxTreeNodeState> {
  const states = new Map<string, CheckboxTreeNodeState>();
  // Whether the node or a node under it is held
  const visit = (node: CheckboxTreeNodeDto, isCovered: boolean): boolean => {
    const isHeld = held.has(node.key);
    let holdsBelow = false;
    for (const child of node.children) {
      holdsBelow = visit(child, isCovered || isHeld) || holdsBelow;
    }
    states.set(node.key, { isChecked: isCovered || isHeld, isCovered, isIndeterminate: !isCovered && !isHeld && holdsBelow });
    return isHeld || holdsBelow;
  };
  for (const node of nodes) {
    visit(node, false);
  }
  return states;
}

/** The key of the node and of every node under it. */
function keysOf(node: CheckboxTreeNodeDto): readonly string[] {
  return [node.key, ...node.children.flatMap(keysOf)];
}
