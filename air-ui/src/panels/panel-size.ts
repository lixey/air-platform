/**
 * Panel sizes are written the way CSS writes them — `320px`, `25%`, `1fr`, `auto` —
 * and turn into a single `flex` shorthand. One size per panel is enough because it
 * always applies along the parent's direction: width in a row, height in a column.
 */

export type PanelSizeUnit = 'px' | '%' | 'fr' | 'auto';

export interface PanelSizeValue {
  readonly value: number;
  readonly unit: PanelSizeUnit;
}

const FILL: PanelSizeValue = { value: 1, unit: 'fr' };

/** Reads a size. Anything unreadable falls back to `1fr`, so a typo cannot blank a screen. */
export function parseSize(size: string | undefined): PanelSizeValue {
  if (!size) {
    return FILL;
  }

  if (size === 'auto') {
    return { value: 0, unit: 'auto' };
  }

  const match = /^(\d+(?:\.\d+)?)(px|%|fr)$/.exec(size);
  if (!match) {
    return FILL;
  }

  return { value: Number(match[1]), unit: match[2] as PanelSizeUnit };
}

/** True when the panel takes leftover space instead of holding a size of its own. */
export function isFill(size: string | undefined): boolean {
  return parseSize(size).unit === 'fr';
}

/** Turns a size into the `flex` shorthand that produces it. A rigid panel is exactly its content, whatever its box does. */
export function toFlex(size: string | undefined, isRigid = false): string {
  const { value, unit } = parseSize(size);

  switch (unit) {
    case 'fr':
      return `${value} 1 0`;
    case 'px':
      return `0 0 ${value}px`;
    case '%':
      return `0 0 ${value}%`;
    case 'auto':
      // As large as its content and no larger; smaller when its box is, so what does not fit scrolls inside the
      // content — unless rigid: then the panels around it take the shortfall, and a nav keeps its buttons
      return isRigid ? '0 0 auto' : '0 1 auto';
  }
}

/**
 * A minimum or maximum, in pixels. Bounds are always pixels — a hard floor written as a
 * share of a box that is itself flexible is a puzzle nobody wants, and the validator says
 * so at the source rather than letting it be quietly ignored here.
 */
export function toBound(size: string | null | undefined): number | undefined {
  if (size == null) {
    return undefined;
  }

  const { value, unit } = parseSize(size);
  return unit === 'px' ? value : undefined;
}

/** Writes pixels back as a size, rounded — nobody wants `319.6836px` in their storage. */
export function fromPixels(pixels: number): string {
  return `${Math.round(pixels)}px`;
}
