import type { WritableSignal } from '@angular/core';
import type { FilterNode, GridRequest } from '../core/generated/contracts';
import type { SelectOption } from './select-option';

/**
 * The field a picker's list was opened from, as the search box of the list's header works it: the two boxes hold the
 * one `text`, and what the list's box says after its pause and every key pressed on it go to the field's own handlers.
 */
export interface SelectField {
  readonly text: WritableSignal<string>;
  searched(text: string): void;
  keydown(event: KeyboardEvent): void;
}

/**
 * What a select picks from: static options, or the rows of a grid service shown by its label
 * columns (`name` unless the column says otherwise); the label names what is being picked.
 */
export interface SelectSource {
  readonly label: string;
  readonly service?: string | null;
  readonly labelPaths?: readonly string[] | null;
  readonly options?: readonly SelectOption[] | null;
  readonly formDraft?: Record<string, unknown> | null;
  readonly patchSourceColumns?: readonly string[] | null;
  readonly referenceFilter?: FilterNode | null;
  /** The grid is asked as a picker, for its own columns (`isReferencePicker`), and its first row is active. */
  readonly isPicker?: boolean;
  /** Whether the select offers an empty (`id: 0`, `'—'`) choice at the top of the list, as a filter on a non-required column does. */
  readonly allowEmpty?: boolean;
  /** The parent every request of the list names: that of the grid a picker stands in, for a source that reads it. */
  readonly parent?: Pick<GridRequest, 'parentService' | 'parentId'> | null;
  /** The field of a picker, whose remote the search box of the list's header is; a select has none. */
  readonly field?: SelectField | null;
}
