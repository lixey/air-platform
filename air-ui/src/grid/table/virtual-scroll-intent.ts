import type { GridRowKey } from '../grid-row';
import type { VirtualRowLayoutEngine } from './virtual-row-layout-engine';

/** How many times a scroll to a rendered, measured row is re-aimed before it is taken as settled. */
export const SCROLL_REFINEMENTS = 3;
/** How many renders an anchor may wait for its row before it is dropped. */
export const ANCHOR_ATTEMPTS = 8;
/** Below this a difference of pixels is rounding, not a move. */
export const PIXEL_TOLERANCE = 0.5;
/** How far the scroll position the browser reports may stray from the one the driver wrote and still be its echo. */
export const SCROLL_ECHO_TOLERANCE = 1;

export interface ScrollAnchor {
  readonly index: number;
  readonly offsetWithinRow: number;
  readonly key: GridRowKey;
}

/** A row to bring into view: aimed by estimates, then re-aimed a few times once the row is rendered and measured. */
export interface RevealIntent {
  readonly kind: 'reveal';
  readonly index: number;
  refinements: number;
}

/**
 * A row to keep in place across new heights: `top` is the scroll position still to write, `scheduled` says a write
 * waits for the next render, and `attempts` bounds how many renders it may wait for its row.
 */
export interface AnchorIntent {
  readonly kind: 'anchor';
  anchor: ScrollAnchor;
  attempts: number;
  top: number | null;
  scheduled: boolean;
}

/** What the driver still aims the scroll at across frames; a reveal and an anchor exclude each other. */
export type Intent = { readonly kind: 'idle' } | RevealIntent | AnchorIntent;

export const IDLE: Intent = { kind: 'idle' };

/** Captures the row currently at the top of the viewport so dynamic height updates keep it stationary. */
export function captureScrollAnchor(
  layout: VirtualRowLayoutEngine,
  viewport: HTMLElement | null,
  bodyViewportHeight: number,
  pendingAnchorTop: number | null,
  latestScrollTop: number,
): ScrollAnchor | null {
  if (layout.length === 0) {
    return null;
  }
  const maxScrollTop = Math.max(0, layout.totalHeight() - bodyViewportHeight);
  const rawScrollTop = pendingAnchorTop ?? viewport?.scrollTop ?? latestScrollTop;
  if (rawScrollTop > maxScrollTop + PIXEL_TOLERANCE) {
    return null;
  }
  const scrollTop = Math.min(maxScrollTop, rawScrollTop);
  const index = layout.findIndexAtOffset(scrollTop);
  const key = layout.keyAt(index);
  if (index < 0 || key === undefined) {
    return null;
  }

  return { index, offsetWithinRow: Math.max(0, scrollTop - layout.prefixHeight(index)), key };
}

/** Resolves an anchor's current row index by key, falling back to index when duplicate keys exist. */
export function resolveAnchorIndex(layout: VirtualRowLayoutEngine, anchor: ScrollAnchor, hasDuplicateKeys: boolean): number {
  const keyed = layout.indexOfKey(anchor.key);
  return keyed ?? (hasDuplicateKeys && anchor.index < layout.length ? anchor.index : -1);
}

/** Computes the updated `scrollTop` that keeps `anchor` in place after row heights change. */
export function computeAnchorScrollTop(
  layout: VirtualRowLayoutEngine,
  anchor: ScrollAnchor,
  hasDuplicateKeys: boolean,
  viewportScrollTop: number,
  bodyViewportHeight: number,
  pendingTop: number | null,
): { readonly top: number | null } | null {
  const index = resolveAnchorIndex(layout, anchor, hasDuplicateKeys);
  if (index < 0) {
    return null;
  }

  const height = layout.heightAt(index) ?? layout.estimatedRowHeight;
  const offsetWithinRow = Math.min(anchor.offsetWithinRow, Math.max(0, height - PIXEL_TOLERANCE));
  const maxScrollTop = Math.max(0, layout.totalHeight() - bodyViewportHeight);
  const nextScrollTop = Math.min(maxScrollTop, layout.prefixHeight(index) + offsetWithinRow);

  const isInPlace = Math.abs((pendingTop ?? viewportScrollTop) - nextScrollTop) <= PIXEL_TOLERANCE;
  return { top: isInPlace ? pendingTop : nextScrollTop };
}

/** Corrects `element.scrollTop` against the rendered DOM rect of `row`; returns `true` when the anchor is settled. */
export function refineAnchorElement(
  element: HTMLElement,
  row: HTMLElement | undefined,
  headerHeightPx: number,
  intent: AnchorIntent,
): boolean {
  intent.attempts -= 1;
  if (!row) {
    return intent.attempts <= 0;
  }

  const rect = row.getBoundingClientRect();
  const bodyTop = element.getBoundingClientRect().top + headerHeightPx;
  const desiredOffset = Math.min(intent.anchor.offsetWithinRow, Math.max(0, rect.height - PIXEL_TOLERANCE));
  const correction = desiredOffset - (bodyTop - rect.top);

  if (Math.abs(correction) <= PIXEL_TOLERANCE) {
    return true;
  }

  const previousScrollTop = element.scrollTop;
  element.scrollTop = previousScrollTop + correction;
  return intent.attempts <= 0 || Math.abs(element.scrollTop - previousScrollTop) <= PIXEL_TOLERANCE;
}

/** Computes the minimum scroll jump to bring `index` into `[currentTop, currentTop + bodyViewportHeight]`, or `null` when settled. */
export function computeRevealScrollTop(
  layout: VirtualRowLayoutEngine,
  index: number,
  viewportScrollTop: number,
  bodyViewportHeight: number,
): number | null {
  const rowTop = layout.prefixHeight(index);
  const rowHeight = layout.heightAt(index) ?? layout.estimatedRowHeight;
  const rowBottom = rowTop + rowHeight;
  const maxScrollTop = Math.max(0, layout.totalHeight() - bodyViewportHeight);
  const currentTop = Math.min(viewportScrollTop, maxScrollTop);
  const currentBottom = currentTop + bodyViewportHeight;

  const isInView = rowTop >= currentTop - PIXEL_TOLERANCE && rowBottom <= currentBottom + PIXEL_TOLERANCE;
  if (isInView) {
    return null;
  }
  const nextScrollTop = Math.min(maxScrollTop, Math.max(0, rowTop < currentTop ? rowTop : rowBottom - bodyViewportHeight));
  return Math.abs(viewportScrollTop - nextScrollTop) <= PIXEL_TOLERANCE ? null : nextScrollTop;
}
