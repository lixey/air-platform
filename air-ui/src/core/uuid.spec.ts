import { afterEach, describe, expect, it, vi } from 'vitest';
import { newUuid } from './uuid';

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

describe('newUuid', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('uses crypto.randomUUID when available in a secure context', () => {
    const fixed = '12345678-1234-4234-8234-123456789abc';
    const randomUUID = vi.fn(() => fixed);
    vi.stubGlobal('crypto', { randomUUID, getRandomValues: globalThis.crypto.getRandomValues.bind(globalThis.crypto) });

    expect(newUuid()).toBe(fixed);
    expect(randomUUID).toHaveBeenCalledTimes(1);
  });

  it('falls back to crypto.getRandomValues in plain HTTP LAN contexts where randomUUID is missing', () => {
    const getRandomValues = vi.fn((buf: Uint8Array) => {
      for (let i = 0; i < buf.length; i++) {
        buf[i] = (i * 17 + 42) & 0xff;
      }
      return buf;
    });
    vi.stubGlobal('crypto', { getRandomValues });

    const id = newUuid();

    expect(id).toMatch(UUID_V4);
    expect(getRandomValues).toHaveBeenCalledTimes(1);
  });
});
