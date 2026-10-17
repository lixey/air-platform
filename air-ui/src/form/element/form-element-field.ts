import type { FormElementDto } from '../../core/generated/contracts';

/**
 * The DOM id of the control an element at `path` draws; its label, help and messages are named after it. `scope` is the
 * prefix its form gives (`FormService.idScope`), which no other form of the document repeats: `form-7`, `ask-8`.
 */
export function fieldIdOf(path: string, scope: string): string {
  return `${scope}-${path}`;
}

/** Whether a text control of the element writes emptied text as null: unless its constraints forbid null. */
export function allowsNullOf(element: FormElementDto): boolean {
  return element.constraints?.allowsNull !== false;
}
