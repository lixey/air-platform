/**
 * Where focus goes around an overlay: into its card when it opens, around inside it while
 * it is on top, and back to whatever opened it when it closes. Plain DOM, no dependency.
 */

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** Whether an element says `aria-disabled` and stands out of the tab order: a disabled button, a select's `Clear` trigger. */
const isOutOfReach = (el: HTMLElement): boolean => el.getAttribute('aria-disabled') === 'true' && el.getAttribute('tabindex') === '-1';

/**
 * The elements inside `root` a user can tab to, in document order: none inside a hidden or inert part. An element
 * that says `aria-disabled` is counted by its tab stop: one that keeps it — a button given a reason, a field that
 * refuses input, a button busy with its request — is listed, as the browser's own `Tab` reaches it, and one at
 * `tabindex="-1"` is not, whatever its tag.
 */
export function focusables(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(el => !isOutOfReach(el) && !el.closest('[hidden],[inert]'));
}

/**
 * Focuses what the card asks for — the element marked `autofocus`, or, for a control's host that is not focusable
 * itself, the first focusable element inside it — else its first focusable element, else the card itself.
 */
export function focusInto(card: HTMLElement): void {
  const asked = card.querySelector<HTMLElement>('[autofocus]');
  const target = asked && !asked.matches(FOCUSABLE) ? (focusables(asked)[0] ?? asked) : asked;
  (target ?? focusables(card)[0] ?? card).focus();
}

/** Keeps `Tab` and `Shift+Tab` inside the card, wrapping at either end. */
export function trapTab(event: Event, card: HTMLElement): void {
  const shiftKey = event instanceof KeyboardEvent && event.shiftKey;
  const list = focusables(card);

  if (list.length === 0) {
    event.preventDefault();
    card.focus();
    return;
  }

  const first = list[0];
  const last = list[list.length - 1];
  const active = document.activeElement;

  if (shiftKey && (active === first || active === card)) {
    event.preventDefault();
    last.focus();
  } else if (!shiftKey && active === last) {
    event.preventDefault();
    first.focus();
  }
}

/** Hands focus back to the opener, if it is still in the document; otherwise nowhere. */
export function restoreFocus(opener: Element | null): void {
  if (opener instanceof HTMLElement && opener.isConnected) {
    opener.focus();
  }
}

/** Whether focus stands on an element other than the opener: the user moved it on while the overlay was open. */
export function hasMovedOn(opener: Element | null): boolean {
  const active = document.activeElement;
  return active !== null && active !== document.body && active !== opener;
}
