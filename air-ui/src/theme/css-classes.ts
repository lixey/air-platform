/**
 * The class strings the library draws with, in one file: the keyboard focus ring, the field, the
 * checkbox. They are Tailwind utilities, so they live with the theme and every folder may read them.
 */

/**
 * The one keyboard focus ring the library draws: the theme's accent, only when focus arrived
 * from the keyboard, and nothing after a pointer click. `outline-none` at rest silences the
 * browser's own ring on anything that is not a native control. A field draws no ring: its
 * border says it has focus (`FIELD_FOCUS`).
 *
 * `FOCUS_RING` draws it inside the element's edge, for buttons, check boxes and rows; the outside
 * variant stands off a thin thing — a divider — that has no room inside.
 */
export const FOCUS_RING =
  'outline-none focus-visible:outline-solid focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent';

export const FOCUS_RING_OUTSIDE =
  'outline-none focus-visible:outline-solid focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent';

/**
 * The focus of a field: the field's own one-pixel border turns `field-focus` on every focus, pointer
 * or keyboard — a field that has the caret says so at once, and nothing is added around it. For
 * text boxes and search boxes; a button keeps the keyboard ring above.
 */
export const FIELD_FOCUS = 'outline-none focus:border-field-focus';

/**
 * The same focus for a field whose box holds the focused element: the select's combobox, a checkbox's
 * enclosure, the group box of a check set or a check tree.
 */
export const FIELD_FOCUS_WITHIN = 'outline-none focus-within:border-field-focus';

/** The border of a field under the pointer: `field-hover`, blue, before any focus. */
export const FIELD_HOVER = 'hover:border-field-hover';

/** The border of an invalid field: the error's hue, as the message under it. */
export const INVALID_EDGE = 'hue-red border-(--hue-ink) hover:border-(--hue-ink)';

/** The classes a text-like field draws: the `field` ground, a border turning `field-hover` under the pointer and `field-focus` on focus. */
export const FIELD_CLASSES = `w-full min-h-9 px-2 rounded-lg border bg-field text-ink text-base ${FIELD_HOVER} ${FIELD_FOCUS}`;

/** How a field is drawn: a box of its own, or as the whole of a grid's cell whose editor it is. */
export type FieldLook = 'box' | 'cell';

/**
 * A field as the whole of a grid's cell: full width and height, its text at the padding of the cell's display, with no
 * border, no radius, no ground and no outline of its own; the editing cell draws the frame and the ground.
 */
export const CELL_FIELD_CLASSES = 'w-full h-full min-h-0 px-2 border-0 rounded-none bg-transparent text-ink text-base outline-none';

/**
 * The standard field classes with the error's border while the field is invalid, `field-edge` otherwise; in a cell's
 * look the cell's field classes whatever `invalid` says, since the cell's frame says it.
 */
export function fieldClasses(invalid: boolean, look: FieldLook = 'box'): string {
  if (look === 'cell') {
    return CELL_FIELD_CLASSES;
  }
  return `${FIELD_CLASSES} ${invalid ? INVALID_EDGE : 'border-field-edge'}`;
}

/**
 * A native number input without the browser's spin arrows: the inner and the outer WebKit spin buttons without
 * appearance, and the appearance of a text field.
 */
export const NO_SPIN_ARROWS =
  '[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none';

/**
 * The one checkbox the library draws — 24 by 24 — for the form and every control outside a grid, filled with `checked`
 * and half-checked with `checked-mixed`. Its look is `.ui-checkbox-input` in `styles.css`.
 */
export const CHECKBOX_CLASSES = `ui-checkbox-input ${FOCUS_RING}`;

/** The same checkbox in a grid — its selection column, its header, its checkbox cells — filled with `row-checked` instead. */
export const GRID_CHECKBOX_CLASSES = `${CHECKBOX_CLASSES} ui-checkbox-row`;

/** The host of a grid header that hangs controls over its label: the group its strip appears in, at the header's full height. */
export const GRID_HEADER_HOST = 'group relative flex items-stretch h-full min-w-0';

/**
 * The strip a grid header hangs its controls in, over the label's right end on `surface-2`: shown on hover after a
 * delay and on focus.
 */
export const GRID_HEADER_STRIP =
  'absolute inset-y-0 right-0 flex h-full shrink-0 bg-surface-2 opacity-0 transition-opacity duration-100 group-hover:opacity-100 group-hover:delay-150 focus-within:opacity-100';

/** The square control a grid header hangs in its strip: the sort, the menu. */
export const GRID_HEADER_BUTTON =
  'flex items-center justify-center h-full w-6 shrink-0 text-muted hover:bg-active hover:text-on-active cursor-pointer outline-none';

/** The quiet clear control of a grid header's strip: no square, `muted` turning `link-hover` under the pointer. */
export const GRID_HEADER_CLEAR =
  'flex items-center justify-center h-full w-4 shrink-0 text-muted hover:text-link-hover cursor-pointer outline-none';

/** A picked value drawn as a chip beside a select's combobox: the blue hue's soft ground, edge and ink. */
export const PICK_CHIP = 'hue-blue bg-(--btn-ground) border border-(--btn-edge) text-(--btn-ink)';

/**
 * The class that puts what it marks, and all it holds, in the work area's slate scale: the routed outlet, an overlay's
 * card opened from the work area, and the sign-in and change-password cards.
 */
export const WORK_AREA = 'ui-work';

/**
 * A segmented control: one soft blue box of the medium height whose buttons stand joined, with no gap between them
 * (the date picker's steppers). A segment's ground and focus mark are square inside the box and follow the box's inner
 * radius on its outer side only, so a hover never shows a pill inside the box nor passes its corners: `SEGMENT_FIRST`
 * and `SEGMENT_LAST` round the first and last segment.
 */
export const SEGMENTED = 'flex h-9 rounded-lg border hue-blue bg-(--btn-ground) border-(--btn-edge)';

/** A segment of a `SEGMENTED` box: its hue's ink, ground under the pointer and the keyboard ring. */
export const SEGMENT =
  'flex items-center justify-center text-base font-medium text-(--btn-ink) hover:bg-(--btn-hover) active:bg-(--btn-active) ' +
  `cursor-pointer select-none ${FOCUS_RING}`;

/**
 * The outer corners of the first control inside a box of the medium radius: a segment, a stepper's button or a clear
 * control.
 */
export const SEGMENT_FIRST = 'rounded-l-[calc(var(--radius-lg)-1px)]';

/**
 * The outer corners of the last control inside a box of the medium radius: a segment, a stepper's button or a clear
 * control.
 */
export const SEGMENT_LAST = 'rounded-r-[calc(var(--radius-lg)-1px)]';

/** A control inside a box under the pointer: the `hover` ground and the `link-hover` icon; a disabled one stays quiet. */
export const INNER_CONTROL_HOVER = 'hover:bg-hover hover:text-link-hover disabled:hover:bg-transparent disabled:hover:text-muted';

/**
 * The ghost segmented control: buttons of the medium height standing joined with no box of their own, grey as a ghost
 * button of the default hue (`ui-btn variant="ghost"`), a button's ground showing only under the pointer and while
 * pressed (a file's actions). Where the row stands on a frame's edges, its outer segments take the frame's inner radius.
 */
export const SEGMENTED_GHOST = 'flex h-9 hue-primary';

/** A segment of a `SEGMENTED_GHOST` row: the ghost button's ink, its ground under the pointer and the keyboard ring. */
export const SEGMENT_GHOST =
  'flex items-center justify-center text-base font-medium text-(--btn-ink) hover:bg-(--btn-hover) active:bg-(--btn-active) ' +
  `cursor-pointer select-none ${FOCUS_RING}`;
