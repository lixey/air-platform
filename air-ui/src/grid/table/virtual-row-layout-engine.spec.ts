import { describe, expect, it } from 'vitest';
import { VirtualRowLayoutEngine } from './virtual-row-layout-engine';

describe('VirtualRowLayoutEngine', () => {
  it('rejects non-positive or non-finite estimated row heights', () => {
    expect(() => new VirtualRowLayoutEngine(0)).toThrow(RangeError);
    expect(() => new VirtualRowLayoutEngine(-36)).toThrow(RangeError);
    expect(() => new VirtualRowLayoutEngine(Number.NaN)).toThrow(RangeError);
    expect(() => new VirtualRowLayoutEngine(Number.POSITIVE_INFINITY)).toThrow(RangeError);
  });

  it('tracks sequence changes, unique key indexes, and ambiguous duplicate keys', () => {
    const engine = new VirtualRowLayoutEngine(36);

    expect(engine.setRows([10, 20, 30])).toEqual({ sequenceChanged: true, hasDuplicateKeys: false });
    expect(engine.length).toBe(3);
    expect(engine.totalHeight()).toBe(108);
    expect(engine.indexOfKey(20)).toBe(1);
    expect(engine.keyAt(2)).toBe(30);
    expect(engine.keyAt(-1)).toBeUndefined();
    expect(engine.keyAt(3)).toBeUndefined();

    // Setting the exact same sequence keeps existing measurements intact.
    engine.updateMeasuredHeight(1, 60);
    expect(engine.setRows([10, 20, 30])).toEqual({ sequenceChanged: false, hasDuplicateKeys: false });
    expect(engine.measuredRowCount).toBe(1);
    expect(engine.heightAt(1)).toBe(60);

    // Duplicate keys clear indexOfKey for the ambiguous key while preserving unique keys.
    expect(engine.setRows([100, 200, 100, 300])).toEqual({ sequenceChanged: true, hasDuplicateKeys: true });
    expect(engine.measuredRowCount).toBe(0);
    expect(engine.indexOfKey(100)).toBeUndefined();
    expect(engine.indexOfKey(200)).toBe(1);
    expect(engine.indexOfKey(300)).toBe(3);
  });

  it('updates Fenwick prefix sums and ignores invalid or out-of-bounds height measurements', () => {
    const engine = new VirtualRowLayoutEngine(40);
    engine.setRows([1, 2, 3, 4]);

    expect(engine.updateMeasuredHeight(-1, 50)).toBe(0);
    expect(engine.updateMeasuredHeight(4, 50)).toBe(0);
    expect(engine.updateMeasuredHeight(1, 0)).toBe(0);
    expect(engine.updateMeasuredHeight(1, Number.NaN)).toBe(0);
    expect(engine.measuredRowCount).toBe(0);

    expect(engine.updateMeasuredHeight(1, 65)).toBe(25);
    expect(engine.isMeasured(1)).toBe(true);
    expect(engine.isMeasured(0)).toBe(false);
    expect(engine.measuredRowCount).toBe(1);

    // Measuring a row whose height equals the estimate still marks it measured with zero delta.
    expect(engine.updateMeasuredHeight(2, 40)).toBe(0);
    expect(engine.isMeasured(2)).toBe(true);
    expect(engine.measuredRowCount).toBe(2);

    expect(engine.prefixHeight(0)).toBe(0);
    expect(engine.prefixHeight(1)).toBe(40);
    expect(engine.prefixHeight(2)).toBe(105);
    expect(engine.prefixHeight(3)).toBe(145);
    expect(engine.totalHeight()).toBe(185);
  });

  it('maps pixel offsets to rows with exact boundaries belonging to the following row', () => {
    const engine = new VirtualRowLayoutEngine(30);
    expect(engine.findIndexAtOffset(0)).toBe(-1);

    engine.setRows([1, 2, 3]);
    engine.updateMeasuredHeight(0, 20);
    engine.updateMeasuredHeight(1, 50);
    // Row extents: [0, 20), [20, 70), [70, 100)

    expect(engine.findIndexAtOffset(-10)).toBe(0);
    expect(engine.findIndexAtOffset(Number.NaN)).toBe(0);
    expect(engine.findIndexAtOffset(0)).toBe(0);
    expect(engine.findIndexAtOffset(19.99)).toBe(0);
    expect(engine.findIndexAtOffset(20)).toBe(1);
    expect(engine.findIndexAtOffset(69.99)).toBe(1);
    expect(engine.findIndexAtOffset(70)).toBe(2);
    expect(engine.findIndexAtOffset(100)).toBe(2);
    expect(engine.findIndexAtOffset(500)).toBe(2);
  });

  it('computes overscanned viewport ranges and exact top/bottom spacers', () => {
    const engine = new VirtualRowLayoutEngine(50);
    expect(engine.rangeForViewport(0, 200)).toEqual({ startIndex: 0, endIndex: 0, topSpacer: 0, bottomSpacer: 0 });

    engine.setRows(Array.from({ length: 10 }, (_, i) => i + 1));
    // Total = 500px; scrollTop = 150, viewport = 100 -> visible [150, 250), overscan = 50 -> render [100, 300) -> rows [2, 6)
    expect(engine.rangeForViewport(150, 100, 50)).toEqual({
      startIndex: 2,
      endIndex: 6,
      topSpacer: 100,
      bottomSpacer: 200,
    });
  });

  it('invalidates off-screen measurements while preserving specified rendered indexes', () => {
    const engine = new VirtualRowLayoutEngine(30);
    engine.setRows([1, 2, 3, 4]);
    engine.updateMeasuredHeight(0, 45);
    engine.updateMeasuredHeight(1, 60);
    engine.updateMeasuredHeight(2, 75);

    engine.invalidateMeasurements([1]);
    expect(engine.measuredRowCount).toBe(1);
    expect(engine.isMeasured(0)).toBe(false);
    expect(engine.isMeasured(1)).toBe(true);
    expect(engine.isMeasured(2)).toBe(false);
    expect(engine.heightAt(0)).toBe(30);
    expect(engine.heightAt(1)).toBe(60);
    expect(engine.totalHeight()).toBe(150);
  });
});
