/**
 * How a phone number is written as it is typed: by the declared mask whose fixed prefix its digits start with (`#` a
 * digit, `+380 (##) ###-##-##`), and stored as `+` and the digits (`+380501234567`).
 */

/** The place of a digit in a mask. */
const DIGIT = '#';

/** The digits of a text, in order. */
export function phoneDigits(text: string): string {
  return text.replace(/\D/g, '');
}

/** The value a phone box writes: `+` and the digits of its text; null for a text without digits. */
export function phoneValue(text: string): string | null {
  const digits = phoneDigits(text);
  return digits === '' ? null : `+${digits}`;
}

/**
 * The digits written by the mask whose fixed prefix — the digits before its first `#` — they start with, the longest
 * such prefix winning; a mask's separators stand only before a digit, and digits past its end follow it. Without such
 * a mask, `+` and the digits.
 */
export function phoneText(digits: string, masks: readonly string[]): string {
  if (digits === '') {
    return '';
  }
  const mask = maskOf(digits, masks);
  return mask === null ? `+${digits}` : written(digits, mask);
}

/** The place in `text` just after its `count`th digit: where the caret stands once the text is written again. */
export function caretAfterDigits(text: string, count: number): number {
  let seen = 0;
  for (let at = 0; at < text.length; at++) {
    if (seen === count) {
      return at;
    }
    if (/\d/.test(text[at])) {
      seen++;
    }
  }
  return text.length;
}

/** The fixed prefix of a mask: the digits before its first `#`, which every mask has (the server refuses one without). */
function prefixOf(mask: string): string {
  return phoneDigits(mask.slice(0, mask.indexOf(DIGIT)));
}

/** The mask with the longest fixed prefix the digits start with; the first of equals. */
function maskOf(digits: string, masks: readonly string[]): string | null {
  let chosen: string | null = null;
  let longest = -1;
  for (const mask of masks) {
    const prefix = prefixOf(mask);
    if (prefix.length > longest && digits.startsWith(prefix)) {
      chosen = mask;
      longest = prefix.length;
    }
  }
  return chosen;
}

/** The digits in the places of the mask: its prefix's digits and each `#` take the next digit, the rest waits for one. */
function written(digits: string, mask: string): string {
  const prefixEnd = mask.indexOf(DIGIT);
  let text = '';
  let pending = '';
  let next = 0;
  for (let at = 0; at < mask.length && next < digits.length; at++) {
    const place = mask[at];
    if (place === DIGIT || (at < prefixEnd && /\d/.test(place))) {
      text += pending + digits[next++];
      pending = '';
    } else {
      pending += place;
    }
  }
  return text + digits.slice(next);
}
