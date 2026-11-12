import type { VirtualRowLayoutEngine } from './virtual-row-layout-engine';
import { PIXEL_TOLERANCE } from './virtual-scroll-intent';

/** Below this a height or a spacer is the same: a measurement that moved by less changes no geometry. */
export const HEIGHT_TOLERANCE = 0.01;

/** What the viewport is taken to be until it is measured. */
const ASSUMED_VIEWPORT_HEIGHT = 600;

/** Reads the integer `data-row-index` attribute of a rendered virtual row element, or `-1` when absent. */
export function indexOfRowElement(row: HTMLElement): number {
  const index = Number.parseInt(row.dataset['rowIndex'] ?? '', 10);
  return Number.isInteger(index) ? index : -1;
}

/** Finds the rendered DOM element currently representing `index`, if mounted. */
export function rowElementAt(rows: readonly HTMLElement[], index: number): HTMLElement | undefined {
  return rows.find(row => indexOfRowElement(row) === index);
}

/**
 * Observes viewport and rendered-row `ResizeObserver` events, batches row height measurements per animation frame, and
 * tracks header/body viewport dimensions for `VirtualScrollDriver`.
 */
export class VirtualScrollObserver {
  private readonly observedRows = new Set<HTMLElement>();
  private readonly pendingRowMeasurements = new Map<HTMLElement, number | null>();
  private rowResizeObserver?: ResizeObserver;
  private viewportResizeObserver?: ResizeObserver;
  private viewportHeightPx = ASSUMED_VIEWPORT_HEIGHT;
  private lastViewportWidth = 0;

  /** The head above the rows; until it is measured, one column header row, as tall as an estimated row. */
  headerHeightPx: number;
  hasDirtyViewportMetrics = true;

  constructor(
    estimatedRowHeight: number,
    private readonly scheduleFrame: () => void,
  ) {
    this.headerHeightPx = estimatedRowHeight;
  }

  get hasPendingRowMeasurements(): boolean {
    return this.pendingRowMeasurements.size > 0;
  }

  bodyViewportHeight(): number {
    return Math.max(1, this.viewportHeightPx - this.headerHeightPx);
  }

  clearPendingMeasurements(): void {
    this.pendingRowMeasurements.clear();
  }

  attach(element: HTMLElement): void {
    if (typeof ResizeObserver === 'undefined') {
      return;
    }
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

  destroy(): void {
    this.viewportResizeObserver?.disconnect();
    this.rowResizeObserver?.disconnect();
    this.observedRows.clear();
    this.pendingRowMeasurements.clear();
  }

  syncObservedRows(renderedRows: readonly HTMLElement[]): void {
    const current = new Set(renderedRows);

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
  }

  queueRenderedRows(renderedRows: readonly HTMLElement[]): void {
    for (const row of renderedRows) {
      this.pendingRowMeasurements.set(row, null);
    }
  }

  applyPendingMeasurements(layout: VirtualRowLayoutEngine): boolean {
    const pending = [...this.pendingRowMeasurements.entries()];
    this.pendingRowMeasurements.clear();
    let hasGeometryChanged = false;

    for (const [row, observedHeight] of pending) {
      if (!row.isConnected) {
        continue;
      }
      const index = indexOfRowElement(row);
      if (index < 0 || index >= layout.length) {
        continue;
      }

      const height = observedHeight ?? row.getBoundingClientRect().height;
      if (!Number.isFinite(height) || height <= 0) {
        continue;
      }

      const current = layout.heightAt(index);
      const isUnchanged = layout.isMeasured(index) && current !== undefined && Math.abs(current - height) <= HEIGHT_TOLERANCE;
      if (isUnchanged) {
        continue;
      }

      const delta = layout.updateMeasuredHeight(index, height);
      hasGeometryChanged ||= Math.abs(delta) > HEIGHT_TOLERANCE;
    }

    return hasGeometryChanged;
  }

  measureViewport(element: HTMLElement | null, onViewportMeasured: () => void): boolean {
    if (!element) {
      return false;
    }

    const height = element.clientHeight;
    const width = element.clientWidth;
    const headerHeight = element.querySelector('thead')?.getBoundingClientRect().height ?? 0;
    onViewportMeasured();
    const widthChanged = this.lastViewportWidth > 0 && Math.abs(width - this.lastViewportWidth) > PIXEL_TOLERANCE;
    this.lastViewportWidth = width;

    if (height > 0) {
      this.viewportHeightPx = height;
    }
    this.headerHeightPx = headerHeight;
    return widthChanged;
  }
}
