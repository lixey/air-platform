import { describe, expect, it } from 'vitest';
import { formatBytes, formatMegabytes } from './file-size';

describe('the size of a file', () => {
  it('is written in bytes, kilobytes or megabytes with the unit of the culture', () => {
    expect(formatBytes(512, 'en-US')).toBe('512 bytes');
    expect(formatBytes(84 * 1024, 'en-US')).toBe('84 kB');
    expect(formatBytes(1258291, 'en-US')).toBe('1.2 MB');
    expect(formatBytes(10 * 1024 * 1024, 'uk-UA')).toBe('10 МБ');
    expect(formatBytes(1572864, 'uk-UA')).toBe('1,5 МБ');
  });

  it('is written in bare megabytes where the text names the unit', () => {
    expect(formatMegabytes(10485760, 'uk-UA')).toBe('10');
    expect(formatMegabytes(1572864, 'uk-UA')).toBe('1,5');
    expect(formatMegabytes(1572864, 'en-US')).toBe('1.5');
  });
});
