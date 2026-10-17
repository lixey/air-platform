import type { GridRowKey } from '../grid-row';

export interface VirtualRowSetResult {
  readonly sequenceChanged: boolean;
  readonly hasDuplicateKeys: boolean;
}

/**
 * The rows to render, `[startIndex, endIndex)`, and the spacers standing for the rest, in CSS pixels.
 */
export interface VirtualRowRange {
  readonly startIndex: number;
  readonly endIndex: number;
  readonly topSpacer: number;
  readonly bottomSpacer: number;
}

/**
 * The row layout of the virtual table: row heights in a Fenwick tree, so a measurement and a
 * pixel-to-row lookup both stay O(log n), and unmeasured rows take the estimate.
 */
export class VirtualRowLayoutEngine {
  readonly estimatedRowHeight: number;

  private keys: readonly GridRowKey[] = [];
  private heights = new Float64Array(0);
  private fenwickTree = new Float64Array(1);
  private measuredRows = new Uint8Array(0);
  private uniqueKeyIndexes = new Map<GridRowKey, number>();
  private ambiguousKeys = new Set<GridRowKey>();
  private _measuredRowCount = 0;

  constructor(estimatedRowHeight: number) {
    if (!Number.isFinite(estimatedRowHeight) || estimatedRowHeight <= 0) {
      throw new RangeError('The estimated row height must be a positive number.');
    }

    this.estimatedRowHeight = estimatedRowHeight;
  }

  get length(): number {
    return this.keys.length;
  }

  get measuredRowCount(): number {
    return this._measuredRowCount;
  }

  setRows(keys: readonly GridRowKey[]): VirtualRowSetResult {
    const sequenceChanged = !this.hasSameSequence(keys);

    if (sequenceChanged) {
      this.keys = [...keys];
      this.heights = new Float64Array(keys.length);
      this.heights.fill(this.estimatedRowHeight);
      this.fenwickTree = new Float64Array(keys.length + 1);
      this.measuredRows = new Uint8Array(keys.length);
      this._measuredRowCount = 0;
      this.rebuildFenwickTree();
      this.rebuildKeyIndex();
    }

    return { sequenceChanged, hasDuplicateKeys: this.ambiguousKeys.size > 0 };
  }

  invalidateMeasurements(preserveIndexes: readonly number[] = []): void {
    if (this.length === 0) {
      return;
    }

    const preservedMeasurements = new Map<number, number>();
    preserveIndexes.forEach(index => {
      if (this.isMeasured(index)) {
        preservedMeasurements.set(index, this.heights[index]);
      }
    });

    this.heights.fill(this.estimatedRowHeight);
    this.measuredRows.fill(0);
    this._measuredRowCount = 0;
    this.rebuildFenwickTree();

    preservedMeasurements.forEach((height, index) => {
      this.updateMeasuredHeight(index, height);
    });
  }

  /**
   * Returns the applied height delta. Stale indexes and invalid DOM
   * measurements are ignored so delayed ResizeObserver entries are harmless.
   */
  updateMeasuredHeight(index: number, height: number): number {
    if (!Number.isInteger(index) || index < 0 || index >= this.length || !Number.isFinite(height) || height <= 0) {
      return 0;
    }

    if (this.measuredRows[index] === 0) {
      this.measuredRows[index] = 1;
      this._measuredRowCount += 1;
    }

    const delta = height - this.heights[index];
    if (delta === 0) {
      return 0;
    }

    this.heights[index] = height;
    this.addToFenwickTree(index, delta);
    return delta;
  }

  isMeasured(index: number): boolean {
    return Number.isInteger(index) && index >= 0 && index < this.length && this.measuredRows[index] === 1;
  }

  heightAt(index: number): number | undefined {
    if (!Number.isInteger(index) || index < 0 || index >= this.length) {
      return undefined;
    }

    return this.heights[index];
  }

  keyAt(index: number): GridRowKey | undefined {
    if (!Number.isInteger(index) || index < 0 || index >= this.length) {
      return undefined;
    }

    return this.keys[index];
  }

  /** Returns undefined when the key is absent or duplicated. */
  indexOfKey(key: GridRowKey): number | undefined {
    return this.uniqueKeyIndexes.get(key);
  }

  prefixHeight(endIndex: number): number {
    const normalizedEndIndex = this.normalizePrefixIndex(endIndex);
    let sum = 0;

    for (let fenwickIndex = normalizedEndIndex; fenwickIndex > 0; fenwickIndex -= fenwickIndex & -fenwickIndex) {
      sum += this.fenwickTree[fenwickIndex];
    }

    return sum;
  }

  totalHeight(): number {
    return this.prefixHeight(this.length);
  }

  /**
   * Returns the row containing the pixel. Exact row boundaries belong to the
   * following row. Values outside the scrollable extent are clamped.
   */
  findIndexAtOffset(offset: number): number {
    if (this.length === 0) {
      return -1;
    }

    if (Number.isNaN(offset) || offset <= 0) {
      return 0;
    }

    const totalHeight = this.totalHeight();
    if (offset >= totalHeight) {
      return this.length - 1;
    }

    let rowCount = 0;
    let prefix = 0;

    for (let step = this.highestFenwickStep(); step > 0; step >>= 1) {
      const candidate = rowCount + step;
      if (candidate <= this.length && prefix + this.fenwickTree[candidate] <= offset) {
        rowCount = candidate;
        prefix += this.fenwickTree[candidate];
      }
    }

    return Math.min(rowCount, this.length - 1);
  }

  rangeForViewport(scrollTop: number, viewportHeight: number, overscanPx = viewportHeight): VirtualRowRange {
    const totalHeight = this.totalHeight();
    if (this.length === 0) {
      return { startIndex: 0, endIndex: 0, topSpacer: 0, bottomSpacer: 0 };
    }

    const normalizedViewportHeight = this.normalizePixelExtent(viewportHeight, totalHeight);
    const normalizedOverscan = this.normalizePixelExtent(overscanPx, totalHeight);
    const maxScrollTop = Math.max(0, totalHeight - normalizedViewportHeight);
    const normalizedScrollTop = this.normalizeScrollTop(scrollTop, maxScrollTop);
    const visibleBottom = Math.min(totalHeight, normalizedScrollTop + normalizedViewportHeight);
    const renderTop = Math.max(0, normalizedScrollTop - normalizedOverscan);
    const renderBottom = Math.min(totalHeight, visibleBottom + normalizedOverscan);

    const startIndex = this.findIndexAtOffset(renderTop);
    const endIndex = Math.max(startIndex + 1, this.endIndexAtOffset(renderBottom));
    const topSpacer = this.prefixHeight(startIndex);
    const renderedBottom = this.prefixHeight(endIndex);

    return { startIndex, endIndex, topSpacer, bottomSpacer: Math.max(0, totalHeight - renderedBottom) };
  }

  private hasSameSequence(keys: readonly GridRowKey[]): boolean {
    if (keys.length !== this.keys.length) {
      return false;
    }

    return keys.every((key, index) => this.keys[index] === key);
  }

  private rebuildKeyIndex(): void {
    this.uniqueKeyIndexes = new Map<GridRowKey, number>();
    this.ambiguousKeys = new Set<GridRowKey>();

    this.keys.forEach((key, index) => {
      if (this.ambiguousKeys.has(key)) {
        return;
      }

      if (this.uniqueKeyIndexes.has(key)) {
        this.uniqueKeyIndexes.delete(key);
        this.ambiguousKeys.add(key);
        return;
      }

      this.uniqueKeyIndexes.set(key, index);
    });
  }

  private rebuildFenwickTree(): void {
    this.fenwickTree.fill(0);

    for (let index = 0; index < this.length; index += 1) {
      const fenwickIndex = index + 1;
      this.fenwickTree[fenwickIndex] += this.heights[index];
      const parentIndex = fenwickIndex + (fenwickIndex & -fenwickIndex);
      if (parentIndex <= this.length) {
        this.fenwickTree[parentIndex] += this.fenwickTree[fenwickIndex];
      }
    }
  }

  private addToFenwickTree(index: number, delta: number): void {
    for (let fenwickIndex = index + 1; fenwickIndex <= this.length; fenwickIndex += fenwickIndex & -fenwickIndex) {
      this.fenwickTree[fenwickIndex] += delta;
    }
  }

  /** Returns the exclusive row end for a right-exclusive pixel boundary. */
  private endIndexAtOffset(offset: number): number {
    if (offset <= 0 || Number.isNaN(offset)) {
      return 0;
    }

    const totalHeight = this.totalHeight();
    if (offset >= totalHeight) {
      return this.length;
    }

    let rowsBeforeBoundary = 0;
    let prefix = 0;

    for (let step = this.highestFenwickStep(); step > 0; step >>= 1) {
      const candidate = rowsBeforeBoundary + step;
      if (candidate <= this.length && prefix + this.fenwickTree[candidate] < offset) {
        rowsBeforeBoundary = candidate;
        prefix += this.fenwickTree[candidate];
      }
    }

    return Math.min(this.length, rowsBeforeBoundary + 1);
  }

  private highestFenwickStep(): number {
    let step = 1;
    while (step * 2 <= this.length) {
      step *= 2;
    }

    return step;
  }

  private normalizePrefixIndex(index: number): number {
    if (Number.isNaN(index) || index <= 0) {
      return 0;
    }

    if (index >= this.length) {
      return this.length;
    }

    return Math.trunc(index);
  }

  private normalizePixelExtent(value: number, totalHeight: number): number {
    if (Number.isNaN(value) || value <= 0) {
      return 0;
    }

    if (!Number.isFinite(value)) {
      return totalHeight;
    }

    return value;
  }

  private normalizeScrollTop(value: number, maxScrollTop: number): number {
    if (Number.isNaN(value) || value <= 0) {
      return 0;
    }

    if (!Number.isFinite(value) || value >= maxScrollTop) {
      return maxScrollTop;
    }

    return value;
  }
}
