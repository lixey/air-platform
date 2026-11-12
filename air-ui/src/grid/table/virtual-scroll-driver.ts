import { signal, type Signal } from '@angular/core';
import type { GridRowKey } from '../grid-row';
import { AnimationFrame } from './animation-frame';
import { VirtualRowLayoutEngine } from './virtual-row-layout-engine';
import {
  ANCHOR_ATTEMPTS,
  captureScrollAnchor,
  computeAnchorScrollTop,
  computeRevealScrollTop,
  IDLE,
  type Intent,
  PIXEL_TOLERANCE,
  refineAnchorElement,
  resolveAnchorIndex,
  type RevealIntent,
  SCROLL_ECHO_TOLERANCE,
  SCROLL_REFINEMENTS,
  type ScrollAnchor,
} from './virtual-scroll-intent';
import { HEIGHT_TOLERANCE, indexOfRowElement, rowElementAt, VirtualScrollObserver } from './virtual-scroll-observer';

export { PIXEL_TOLERANCE };

/** The quiet time after a column width changes before off-screen heights are forgotten. */
const COLUMN_LAYOUT_SETTLE_MS = 120;

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
 * refined once the row is rendered and measured, and while it lasts no row is anchored.
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
  private readonly observer: VirtualScrollObserver;
  private _hasDuplicateKeys = false;
  private latestScrollTop = 0;
  private readonly frame = new AnimationFrame();
  private intent: Intent = IDLE;
  /** The scroll position the driver wrote last: the scroll event that reports it is not the user's. */
  private echo: number | null = null;
  private columnLayoutTimer: ReturnType<typeof setTimeout> | null = null;
  private isDestroyed = false;
  private heardViewport: HTMLElement | null = null;

  constructor(
    private readonly host: VirtualScrollHost,
    estimatedRowHeight: number,
  ) {
    this.layout = new VirtualRowLayoutEngine(estimatedRowHeight);
    this.observer = new VirtualScrollObserver(estimatedRowHeight, () => this.scheduleFrame());
  }

  /** Whether two rows of the sequence share a key, so the host tracks them by index instead. */
  get hasDuplicateKeys(): boolean {
    return this._hasDuplicateKeys;
  }

  /** How many rows a page key moves by: what the body shows at the estimated height, less one to keep the context. */
  pageRows(): number {
    return Math.max(1, Math.floor(this.observer.bodyViewportHeight() / this.layout.estimatedRowHeight) - 1);
  }

  /** The keys of the rows the page holds, in order: a new sequence starts the window from the top. */
  setRows(keys: readonly GridRowKey[]): void {
    const result = this.layout.setRows(keys);
    this._hasDuplicateKeys = result.hasDuplicateKeys;

    if (result.sequenceChanged) {
      this.observer.clearPendingMeasurements();
      this.resetScrollToTop();
    }

    this.commitVirtualRange();
    this.queueRenderedRowsForMeasurement();
    this.scheduleFrame();
  }

  /** Watches the viewport once it is in the DOM and hears its scroll; later calls do nothing. */
  attach(): void {
    const element = this.host.viewport();
    if (!element || this.heardViewport) {
      return;
    }
    this.heardViewport = element;
    element.addEventListener('scroll', this.onViewportScroll, { passive: true });
    this.observer.attach(element);
    this.observer.hasDirtyViewportMetrics = true;
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
    this.observer.destroy();
    this.frame.cancel();
    if (this.columnLayoutTimer !== null) {
      clearTimeout(this.columnLayoutTimer);
      this.columnLayoutTimer = null;
    }
  }

  /** Rows that entered the DOM are observed and queued for measurement, rows that left it forgotten. */
  syncObservedRows(): void {
    this.observer.syncObservedRows(this.host.renderedRows());
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

    const reveal: RevealIntent = { kind: 'reveal', index, refinements: SCROLL_REFINEMENTS };
    this.intent = reveal;
    if (this.applyReveal(reveal) === 'settled') {
      this.intent = IDLE;
    }
  }

  /** The viewport scrolled: a scroll the driver did not make itself cancels what it was still aiming at. */
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

  private scheduleFrame(): void {
    if (!this.isDestroyed) {
      this.frame.schedule(() => this.flushFrame());
    }
  }

  private flushFrame(): void {
    if (this.observer.hasDirtyViewportMetrics) {
      this.observer.hasDirtyViewportMetrics = false;
      this.updateViewportMetrics();
    }
    const measured = this.flushRowMeasurements();
    this.commitVirtualRange();
    if (measured) {
      this.refineReveal();
    }
  }

  private flushRowMeasurements(): boolean {
    if (!this.observer.hasPendingRowMeasurements) {
      return false;
    }
    const anchor =
      this.intent.kind === 'anchor'
        ? this.intent.anchor
        : captureScrollAnchor(
            this.layout,
            this.host.viewport(),
            this.observer.bodyViewportHeight(),
            this.anchorTop(),
            this.latestScrollTop,
          );
    const hasGeometryChanged = this.observer.applyPendingMeasurements(this.layout);
    if (hasGeometryChanged && anchor) {
      this.restoreScrollAnchor(anchor);
    }
    return true;
  }

  private restoreScrollAnchor(anchor: ScrollAnchor): void {
    const element = this.host.viewport();
    if (this.intent.kind === 'reveal' || !element || this.layout.length === 0) {
      return;
    }

    const computed = computeAnchorScrollTop(
      this.layout,
      anchor,
      this._hasDuplicateKeys,
      element.scrollTop,
      this.observer.bodyViewportHeight(),
      this.anchorTop(),
    );
    if (!computed) {
      return;
    }

    const intent = this.intent;
    if (intent.kind === 'anchor') {
      intent.anchor = anchor;
      intent.attempts = ANCHOR_ATTEMPTS;
      intent.top = computed.top;
    } else {
      this.intent = { kind: 'anchor', anchor, attempts: ANCHOR_ATTEMPTS, top: computed.top, scheduled: false };
    }
  }

  private schedulePendingAnchorScroll(): void {
    const intent = this.intent;
    if (this.isDestroyed || intent.kind !== 'anchor' || intent.scheduled) {
      return;
    }

    intent.scheduled = true;
    this.host.afterRender(() => {
      intent.scheduled = false;
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
    const index = resolveAnchorIndex(this.layout, intent.anchor, this._hasDuplicateKeys);
    if (refineAnchorElement(element, rowElementAt(this.host.renderedRows(), index), this.observer.headerHeightPx, intent)) {
      this.intent = IDLE;
    }
    const appliedScrollTop = element.scrollTop;
    this.latestScrollTop = appliedScrollTop;
    this.echo = Math.abs(appliedScrollTop - previousScrollTop) > PIXEL_TOLERANCE ? appliedScrollTop : null;
    this.scheduleFrame();
  }

  private commitVirtualRange(): void {
    const total = this.host.rowCount();
    const viewportHeight = this.observer.bodyViewportHeight();
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
      this.applyPendingAnchorScroll();
      return;
    }

    this._startIndex.set(startIndex);
    this._endIndex.set(endIndex);
    this._topSpacer.set(topSpacer);
    this._bottomSpacer.set(bottomSpacer);
    this.schedulePendingAnchorScroll();
  }

  private queueRenderedRowsForMeasurement(): void {
    this.observer.queueRenderedRows(this.host.renderedRows());
    this.scheduleFrame();
  }

  private invalidateOffscreenMeasurements(): void {
    if (this.layout.measuredRowCount === 0) {
      this.queueRenderedRowsForMeasurement();
      return;
    }

    const anchor = captureScrollAnchor(
      this.layout,
      this.host.viewport(),
      this.observer.bodyViewportHeight(),
      this.anchorTop(),
      this.latestScrollTop,
    );
    const rendered = this.host
      .renderedRows()
      .map(row => indexOfRowElement(row))
      .filter(index => index >= 0);
    this.layout.invalidateMeasurements(rendered);
    if (anchor) {
      this.restoreScrollAnchor(anchor);
    }
    this.queueRenderedRowsForMeasurement();
  }

  private updateViewportMetrics(): void {
    if (this.observer.measureViewport(this.host.viewport(), () => this.host.viewportMeasured())) {
      this.invalidateOffscreenMeasurements();
    }
  }

  private applyReveal(reveal: RevealIntent): 'deferred' | 'scrolled' | 'settled' {
    const element = this.host.viewport();
    if (!element || this.layout.length !== this.host.rowCount()) {
      return 'deferred';
    }

    const nextScrollTop = computeRevealScrollTop(this.layout, reveal.index, element.scrollTop, this.observer.bodyViewportHeight());
    if (nextScrollTop === null) {
      return 'settled';
    }

    this.echo = nextScrollTop;
    element.scrollTop = nextScrollTop;
    this.latestScrollTop = nextScrollTop;
    this.scheduleFrame();
    return 'scrolled';
  }

  private refineReveal(): void {
    const reveal = this.intent;
    if (reveal.kind !== 'reveal') {
      return;
    }
    if (!rowElementAt(this.host.renderedRows(), reveal.index)) {
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

  private anchorTop(): number | null {
    return this.intent.kind === 'anchor' ? this.intent.top : null;
  }
}
