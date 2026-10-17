import type { UiColor } from '../core/generated/contracts';

/** A hue of the library: the server's vocabulary `UiColor`, declared by the generated contracts. */
export type { UiColor } from '../core/generated/contracts';

/** Every hue once: a hue the server adds or drops fails the compile here until the record follows. */
const HUES = {
  primary: true,
  brand: true,
  slate: true,
  gray: true,
  zinc: true,
  neutral: true,
  stone: true,
  red: true,
  orange: true,
  amber: true,
  yellow: true,
  lime: true,
  green: true,
  emerald: true,
  teal: true,
  cyan: true,
  sky: true,
  blue: true,
  indigo: true,
  violet: true,
  purple: true,
  fuchsia: true,
  pink: true,
  rose: true,
} as const satisfies Record<UiColor, true>;

export const UI_COLORS: readonly UiColor[] = Object.keys(HUES) as UiColor[];
