import { signal, type Signal } from '@angular/core';
import type { GridRowKey } from '../grid-row';
import { AnimationFrame } from './animation-frame';
import { VirtualRowLayoutEngine } from './virtual-row-layout-engine';

/** How many times a scroll to a rendered, measured row is re-aimed before it is taken as settled. */
const SCROLL_REFINEMENTS = 3;
/** How many renders an anchor may wait for its row before it is dropped. */
const ANCHOR_ATTEMPTS = 8;
/** The quiet time after a column width changes before off-screen heights are forgotten. */
const COLUMN_LAYOUT_SETTLE_MS = 120;
/** Below this a difference of pixels is rounding, not a move. */
export const PIXEL_TOLERANCE = 0.5;
/** Below this a height or a spacer is the same: a measurement that moved by less changes no geometry. */
const HEIGHT_TOLERANCE = 0.01;
/** How far the scroll position the browser reports may stray from the one the driver wrote and still be its echo. */
const SCROLL_ECHO_TOLERANCE = 1;
/** What the viewport is taken to be until it is measured. */
const ASSUMED_VIEWPORT_HEIGHT = 600;

interface ScrollAnchor {
  readonly index: number;
  readonly offsetWithinRow: number;
  readonly key: GridRowKey;
}

/** A row to bring into view: aimed by estimates, then re-aimed a few times once the row is rendered and measured. */
interface RevealIntent {
  readonly kind: 'reveal';
  readonly index: number;
  refinements: number;
}

/**
 * A row to keep in place across new heights: `top` is the scroll position still to write, `scheduled` says a write
 * waits for the next render, and `attempts` bounds how many renders it may wait for its row.
 */
interface AnchorIntent {
  readonly kind: 'anchor';
  anchor: ScrollAnchor;
  attempts: number;
  top: number | null;
  scheduled: boolean;
}

/** What the driver still aims the scroll at across frames; a reveal and an anchor exclude each other. */
type Intent = { readonly kind: 'idle' } | RevealIntent | AnchorIntent;

const IDLE: Intent = { kind: 'idle' };

/** What the driver needs of the component that draws the rows. */
export interface VirtualScrollHost {
  /** The scrolling element; null until it is in the DOM. */
  viewport(): HTMLElement | null;
  /** The row elements in the DOM now, each carrying its index in `data-row-index`. */
  renderedRows(): readonly HTMLElement[];
  rowCount(): number;
  /** Runs once the next render is done: a scroll correction waits for the window it was computed for. */
  afterRender(run: () => void): void;
  /** Called whenever the viewport's metrics are read, for what the host measures in the same pass. */
  viewportMeasured(): void;
}

/**
 * Drives a virtual list over `VirtualRowLayoutEngine`: which rows are in the DOM and what the spacers
 * stand for, one measuring frame at a time; every rendered row is measured, and the scroll position is
 * anchored to a row across measurements; a request to bring a row into view is aimed by estimates and
 * refined once the row is rendered and measured, and while it lasts no row is anchored. It reads the DOM
 * through its host, hears the viewport's scroll itself and writes only the viewport's `scrollTop`.
 */
export class VirtualScrollDriver {
  private readonly _startIndex = signal(0);
  private readonly _endIndex = signal(0);
  private readonly _topSpacer = signal(0);
  private readonly _bottomSpacer = signal(0);
  /** The window: rows `[startIndex, endIndex)` are drawn, the spacers stand for the rest. */
  readonly startIndex: Signal<number> = this._startIndex.asReadonly();
  readonly endIndex: Signal<number> = this._endIndex.asReadonly();
  readonly topSpacer: Signal<number> = this._topSpacer.asReadonly();
  readonly bottomSpacer: Signal<number> = this._bottomSpacer.asReadonly();

  private readonly layout: VirtualRowLayoutEngine;
  private readonly observedRows = new Set<HTMLElement>();
  private readonly pendingRowMeasurements = new Map<HTMLElement, number | null>();
  private rowResizeObserver?: ResizeObserver;
  private viewportResizeObserver?: ResizeObserver;
  private _hasDuplicateKeys = false;
  private viewportHeightPx = ASSUMED_VIEWPORT_HEIGHT;
  /** The head above the rows; until it is measured, one column header row, as tall as a row. */
  private headerHeightPx: number;
  private lastViewportWidth = 0;
  private latestScrollTop = 0;
  private readonly frame = new AnimationFrame();
  private hasDirtyViewportMetrics = true;
  private intent: Intent = IDLE;
  /** The scroll position the driver wrote last: the scroll event that reports it is not the user's. */
  private echo: number | null = null;
  private columnLayoutTimer: ReturnType<typeof setTimeout> | null = null;
  private isDestroyed = false;

  /** The viewport whose scroll the driver hears, from `attach()` to `destroy()`. */
  private heardViewport: HTMLElement | null = null;

  constructor(
    private readonly host: VirtualScrollHost,
    estimatedRowHeight: number,
  ) {
    this.layout = new VirtualRowLayoutEngine(estimatedRowHeight);
    this.headerHeightPx = estimatedRowHeight;
  }

  /** Whether two rows of the sequence share a key, so the host tracks them by index instead. */
  get hasDuplicateKeys(): boolean {
    return this._hasDuplicateKeys;
  }

  /** How many rows a page key moves by: what the body shows at the estimated height, less one to keep the context. */
  pageRows(): number {
    return Math.max(1, Math.floor(this.bodyViewportHeight() / this.layout.estimatedRowHeight) - 1);
  }

  /** The keys of the rows the page holds, in order: a new sequence starts the window from the top. */
  setRows(keys: readonly GridRowKey[]): void {
    const result = this.layout.setRows(keys);
    this._hasDuplicateKeys = result.hasDuplicateKeys;

    if (result.sequenceChanged) {
      this.pendingRowMeasurements.clear();
      this.resetScrollToTop();
    }

    this.commitVirtualRange();
    this.queueRenderedRowsForMeasurement();
    this.scheduleFrame();
  }

  /**
   * Watches the viewport once it is in the DOM and hears its scroll; later calls do nothing. The listener is the
   * driver's own and passive: a scroll that moves no row then writes no signal and marks no view of the host.
   */
  attach(): void {
    const element = this.host.viewport();
    if (!element || this.heardViewport) {
      return;
    }
    this.heardViewport = element;
    element.addEventListener('scroll', this.onViewportScroll, { passive: true });

    if (typeof ResizeObserver !== 'undefined') {
      this.viewportResizeObserver = new ResizeObserver(() => {
        this.hasDirtyViewportMetrics = true;
        this.scheduleFrame();
      });
      this.viewportResizeObserver.observe(element);

      this.rowResizeObserver = new ResizeObserver(entries => {
        for (const entry of entries) {
          const height = entry.borderBoxSize?.[0]?.blockSize;
          this.pendingRowMeasurements.set(entry.target as HTMLElement, Number.isFinite(height) && height > 0 ? height : null);
        }
        this.scheduleFrame();
      });
    }

    this.hasDirtyViewportMetrics = true;
    this.updateViewportMetrics();
    this.syncObservedRows();
    if (this.intent.kind === 'reveal') {
      this.scrollToIndex(this.intent.index);
    }
    this.scheduleFrame();
  }

  destroy(): void {
    this.isDestroyed = true;
    this.heardViewport?.removeEventListener('scroll', this.onViewportScroll);
    this.viewportResizeObserver?.disconnect();
    this.rowResizeObserver?.disconnect();
    this.observedRows.clear();
    this.pendingRowMeasurements.clear();
    this.frame.cancel();
    if (this.columnLayoutTimer !== null) {
      clearTimeout(this.columnLayoutTimer);
      this.columnLayoutTimer = null;
    }
  }

  /**
   * Rows that entered the DOM are observed and queued for measurement, rows that left it forgotten; a row that stays is
   * measured again only when its observer reports a new size.
   */
  syncObservedRows(): void {
    const current = new Set(this.host.renderedRows());

    for (const row of this.observedRows) {
      if (!current.has(row)) {
        this.rowResizeObserver?.unobserve(row);
        this.observedRows.delete(row);
        this.pendingRowMeasurements.delete(row);
      }
    }

    for (const row of current) {
      if (!this.observedRows.has(row)) {
        this.observedRows.add(row);
        this.rowResizeObserver?.observe(row, { box: 'border-box' });
        this.pendingRowMeasurements.set(row, null);
      }
    }

    this.scheduleFrame();
  }

  /** Other column widths make other row heights: the rendered rows are measured again now, the rest as they come in. */
  invalidateColumnLayout(): void {
    this.queueRenderedRowsForMeasurement();
    if (this.columnLayoutTimer !== null) {
      clearTimeout(this.columnLayoutTimer);
    }
    this.columnLayoutTimer = setTimeout(() => {
      this.columnLayoutTimer = null;
      this.invalidateOffscreenMeasurements();
    }, COLUMN_LAYOUT_SETTLE_MS);
  }

  /** Scrolls the least that brings the row into view; a row already in view stays where it is. */
  scrollToIndex(index: number): void {
    if (!Number.isInteger(index) || index < 0 || index >= this.host.rowCount()) {
      return;
    }

    // A reveal takes the scroll over: the anchor still in flight would pull it back to its row
    const reveal: RevealIntent = { kind: 'reveal', index, refinements: SCROLL_REFINEMENTS };
    this.intent = reveal;
    if (this.applyReveal(reveal) === 'settled') {
      this.intent = IDLE;
    }
  }

  /**
   * The viewport scrolled: a scroll the driver did not make itself cancels what it was still aiming at. Called by the
   * listener `attach()` adds.
   */
  onScroll(): void {
    const element = this.host.viewport();
    if (!element) {
      return;
    }

    const nextScrollTop = element.scrollTop;
    const isEcho = this.echo !== null && Math.abs(nextScrollTop - this.echo) <= SCROLL_ECHO_TOLERANCE;
    const moved = Math.abs(nextScrollTop - this.latestScrollTop) > PIXEL_TOLERANCE;

    if (isEcho) {
      this.echo = null;
    } else if (moved) {
      this.intent = IDLE;
      this.echo = null;
    }

    this.latestScrollTop = nextScrollTop;
    this.scheduleFrame();
  }

  private readonly onViewportScroll = (): void => this.onScroll();

  private resetScrollToTop(): void {
    this.intent = IDLE;
    const element = this.host.viewport();
    if (element && element.scrollTop !== 0) {
      this.echo = 0;
      element.scrollTop = 0;
    } else {
      this.echo = null;
    }
    this.latestScrollTop = 0;
  }

  /** The next frame, by an animation frame or a short timer where frames do not come; none once destroyed. */
  private scheduleFrame(): void {
    if (!this.isDestroyed) {
      this.frame.schedule(() => this.flushFrame());
    }
  }

  private flushFrame(): void {
    if (this.hasDirtyViewportMetrics) {
      this.hasDirtyViewportMetrics = false;
      this.updateViewportMetrics();
    }
    const measured = this.flushRowMeasurements();
    this.commitVirtualRange();
    if (measured) {
      this.refineReveal();
    }
  }

  private flushRowMeasurements(): boolean {
    if (this.pendingRowMeasurements.size === 0) {
      return false;
    }

    const pending = [...this.pendingRowMeasurements.entries()];
    this.pendingRowMeasurements.clear();

    // An anchor in flight stays authoritative while the freshly rendered window is measured
    const anchor = this.intent.kind === 'anchor' ? this.intent.anchor : this.captureScrollAnchor();
    let hasGeometryChanged = false;

    for (const [row, observedHeight] of pending) {
      if (!row.isConnected) {
        continue;
      }
      const index = this.indexOfRowElement(row);
      if (index < 0 || index >= this.layout.length) {
        continue;
      }

      const height = observedHeight ?? row.getBoundingClientRect().height;
      if (!Number.isFinite(height) || height <= 0) {
        continue;
      }

      const current = this.layout.heightAt(index);
      const isUnchanged = this.layout.isMeasured(index) && current !== undefined && Math.abs(current - height) <= HEIGHT_TOLERANCE;
      if (isUnchanged) {
        continue;
      }

      const delta = this.layout.updateMeasuredHeight(index, height);
      hasGeometryChanged ||= Math.abs(delta) > HEIGHT_TOLERANCE;
    }

    if (hasGeometryChanged && anchor) {
      this.restoreScrollAnchor(anchor);
    }
    return true;
  }

  private captureScrollAnchor(): ScrollAnchor | null {
    if (this.layout.length === 0) {
      return null;
    }
    const element = this.host.viewport();
    const maxScrollTop = Math.max(0, this.layout.totalHeight() - this.bodyViewportHeight());
    const rawScrollTop = this.anchorTop() ?? element?.scrollTop ?? this.latestScrollTop;
    if (rawScrollTop > maxScrollTop + PIXEL_TOLERANCE) {
      return null;
    }
    const scrollTop = Math.min(maxScrollTop, rawScrollTop);
    const index = this.layout.findIndexAtOffset(scrollTop);
    const key = this.layout.keyAt(index);
    if (index < 0 || key === undefined) {
      return null;
    }

    return { index, offsetWithinRow: Math.max(0, scrollTop - this.layout.prefixHeight(index)), key };
  }

  /** Keeps the anchor's row where it stood; a reveal is left alone, since it aims at its own row once the heights are in. */
  private restoreScrollAnchor(anchor: ScrollAnchor): void {
    const element = this.host.viewport();
    if (this.intent.kind === 'reveal' || !element || this.layout.length === 0) {
      return;
    }

    const index = this.resolveAnchorIndex(anchor);
    if (index < 0) {
      return;
    }

    const height = this.layout.heightAt(index) ?? this.layout.estimatedRowHeight;
    const offsetWithinRow = Math.min(anchor.offsetWithinRow, Math.max(0, height - PIXEL_TOLERANCE));
    const maxScrollTop = Math.max(0, this.layout.totalHeight() - this.bodyViewportHeight());
    const nextScrollTop = Math.min(maxScrollTop, this.layout.prefixHeight(index) + offsetWithinRow);

    const pendingTop = this.anchorTop();
    const isInPlace = Math.abs((pendingTop ?? element.scrollTop) - nextScrollTop) <= PIXEL_TOLERANCE;
    const top = isInPlace ? pendingTop : nextScrollTop;
    const intent = this.intent;
    if (intent.kind === 'anchor') {
      // Updated in place: the write it already scheduled for the next render stays its own
      intent.anchor = anchor;
      intent.attempts = ANCHOR_ATTEMPTS;
      intent.top = top;
    } else {
      this.intent = { kind: 'anchor', anchor, attempts: ANCHOR_ATTEMPTS, top, scheduled: false };
    }
  }

  private resolveAnchorIndex(anchor: ScrollAnchor): number {
    const keyed = this.layout.indexOfKey(anchor.key);
    return keyed ?? (this._hasDuplicateKeys && anchor.index < this.layout.length ? anchor.index : -1);
  }

  private schedulePendingAnchorScroll(): void {
    const intent = this.intent;
    const hasNothingToSchedule = this.isDestroyed || intent.kind !== 'anchor' || intent.scheduled;
    if (hasNothingToSchedule) {
      return;
    }

    intent.scheduled = true;
    this.host.afterRender(() => {
      intent.scheduled = false;
      // An anchor the user's scroll or a reveal replaced meanwhile is not written
      if (!this.isDestroyed && this.intent === intent) {
        this.applyPendingAnchorScroll();
      }
    });
  }

  private applyPendingAnchorScroll(): void {
    const intent = this.intent;
    const element = this.host.viewport();
    if (intent.kind !== 'anchor' || !element) {
      return;
    }

    const nextScrollTop = intent.top;
    intent.top = null;
    const previousScrollTop = element.scrollTop;
    if (nextScrollTop !== null) {
      element.scrollTop = nextScrollTop;
    }
    this.refineAnchorVisually(element, intent);
    const appliedScrollTop = element.scrollTop;
    this.latestScrollTop = appliedScrollTop;
    this.echo = Math.abs(appliedScrollTop - previousScrollTop) > PIXEL_TOLERANCE ? appliedScrollTop : null;
    this.scheduleFrame();
  }

  /** Corrects the scroll by where the anchor's row is drawn; the anchor ends once it stands, or its attempts run out. */
  private refineAnchorVisually(element: HTMLElement, intent: AnchorIntent): void {
    const index = this.resolveAnchorIndex(intent.anchor);
    const row = this.rowElementAt(index);
    intent.attempts -= 1;
    if (!row) {
      if (intent.attempts <= 0) {
        this.intent = IDLE;
      }
      return;
    }

    const rect = row.getBoundingClientRect();
    const bodyTop = element.getBoundingClientRect().top + this.headerHeightPx;
    const desiredOffset = Math.min(intent.anchor.offsetWithinRow, Math.max(0, rect.height - PIXEL_TOLERANCE));
    const correction = desiredOffset - (bodyTop - rect.top);

    if (Math.abs(correction) <= PIXEL_TOLERANCE) {
      this.intent = IDLE;
      return;
    }

    const previousScrollTop = element.scrollTop;
    element.scrollTop = previousScrollTop + correction;
    if (intent.attempts <= 0 || Math.abs(element.scrollTop - previousScrollTop) <= PIXEL_TOLERANCE) {
      this.intent = IDLE;
    }
  }

  private commitVirtualRange(): void {
    const total = this.host.rowCount();
    const viewportHeight = this.bodyViewportHeight();
    const scrollTop = this.anchorTop() ?? this.latestScrollTop;

    let startIndex = 0;
    let endIndex = 0;
    let topSpacer = 0;
    let bottomSpacer = 0;

    if (total > 0) {
      const range = this.layout.rangeForViewport(scrollTop, viewportHeight, viewportHeight);
      startIndex = range.startIndex;
      endIndex = range.endIndex;
      topSpacer = range.topSpacer;
      bottomSpacer = range.bottomSpacer;
    }

    const changed =
      this.startIndex() !== startIndex ||
      this.endIndex() !== endIndex ||
      Math.abs(this.topSpacer() - topSpacer) > HEIGHT_TOLERANCE ||
      Math.abs(this.bottomSpacer() - bottomSpacer) > HEIGHT_TOLERANCE;

    if (!changed) {
      // The DOM already stands for this geometry, so the correction needs no render between
      this.applyPendingAnchorScroll();
      return;
    }

    this._startIndex.set(startIndex);
    this._endIndex.set(endIndex);
    this._topSpacer.set(topSpacer);
    this._bottomSpacer.set(bottomSpacer);
    this.schedulePendingAnchorScroll();
  }

  private bodyViewportHeight(): number {
    return Math.max(1, this.viewportHeightPx - this.headerHeightPx);
  }

  private queueRenderedRowsForMeasurement(): void {
    for (const row of this.host.renderedRows()) {
      this.pendingRowMeasurements.set(row, null);
    }
    this.scheduleFrame();
  }

  private invalidateOffscreenMeasurements(): void {
    if (this.layout.measuredRowCount === 0) {
      this.queueRenderedRowsForMeasurement();
      return;
    }

    const anchor = this.captureScrollAnchor();
    const rendered = this.host
      .renderedRows()
      .map(row => this.indexOfRowElement(row))
      .filter(index => index >= 0);
    this.layout.invalidateMeasurements(rendered);
    if (anchor) {
      this.restoreScrollAnchor(anchor);
    }
    this.queueRenderedRowsForMeasurement();
  }

  private updateViewportMetrics(): void {
    const element = this.host.viewport();
    if (!element) {
      return;
    }

    const height = element.clientHeight;
    const width = element.clientWidth;
    const headerHeight = element.querySelector('thead')?.getBoundingClientRect().height ?? 0;
    // What the host keeps beside the rows is read in the same pass: the window may have crossed a breakpoint
    this.host.viewportMeasured();
    const widthChanged = this.lastViewportWidth > 0 && Math.abs(width - this.lastViewportWidth) > PIXEL_TOLERANCE;
    this.lastViewportWidth = width;

    if (height > 0) {
      this.viewportHeightPx = height;
    }
    this.headerHeightPx = headerHeight;
    if (widthChanged) {
      this.invalidateOffscreenMeasurements();
    }
  }

  private applyReveal(reveal: RevealIntent): 'deferred' | 'scrolled' | 'settled' {
    const { index } = reveal;
    const element = this.host.viewport();
    if (!element || this.layout.length !== this.host.rowCount()) {
      return 'deferred';
    }

    const viewportHeight = this.bodyViewportHeight();
    const rowTop = this.layout.prefixHeight(index);
    const rowHeight = this.layout.heightAt(index) ?? this.layout.estimatedRowHeight;
    const rowBottom = rowTop + rowHeight;
    const maxScrollTop = Math.max(0, this.layout.totalHeight() - viewportHeight);
    const currentTop = Math.min(element.scrollTop, maxScrollTop);
    const currentBottom = currentTop + viewportHeight;

    const isInView = rowTop >= currentTop - PIXEL_TOLERANCE && rowBottom <= currentBottom + PIXEL_TOLERANCE;
    if (isInView) {
      return 'settled';
    }
    const nextScrollTop = Math.min(maxScrollTop, Math.max(0, rowTop < currentTop ? rowTop : rowBottom - viewportHeight));
    if (Math.abs(element.scrollTop - nextScrollTop) <= PIXEL_TOLERANCE) {
      return 'settled';
    }

    this.echo = nextScrollTop;
    element.scrollTop = nextScrollTop;
    this.latestScrollTop = nextScrollTop;
    this.scheduleFrame();
    return 'scrolled';
  }

  /**
   * After a measurement batch, a pending reveal is applied again: while its target row
   * is not rendered the geometry above it has just changed and the reveal is re-aimed, as many
   * times as it takes; once the target is rendered and measured, up to three refinements settle it.
   */
  private refineReveal(): void {
    const reveal = this.intent;
    if (reveal.kind !== 'reveal') {
      return;
    }
    if (!this.rowElementAt(reveal.index)) {
      // Aimed by estimates: kept pending whatever the result, since only a rendered, measured row settles it
      this.applyReveal(reveal);
      return;
    }
    if (!this.layout.isMeasured(reveal.index) || reveal.refinements <= 0) {
      return;
    }

    reveal.refinements -= 1;
    const result = this.applyReveal(reveal);
    if (result === 'settled' || reveal.refinements === 0) {
      this.intent = IDLE;
    }
  }

  /** The scroll position an anchor still waits to write; null without one. */
  private anchorTop(): number | null {
    return this.intent.kind === 'anchor' ? this.intent.top : null;
  }

  private indexOfRowElement(row: HTMLElement): number {
    const index = Number.parseInt(row.dataset['rowIndex'] ?? '', 10);
    return Number.isInteger(index) ? index : -1;
  }

  private rowElementAt(index: number): HTMLElement | undefined {
    return this.host.renderedRows().find(row => this.indexOfRowElement(row) === index);
  }
}
