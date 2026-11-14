import { newUuid } from './uuid';

/**
 * Bounded 16-slot FIFO cache of unanswered mutation `Idempotency-Key` values keyed by request fingerprint.
 * Taking a fingerprint consumes its retained key once (or mints a fresh UUID); keeping re-inserts it on an `'unknown'`
 * network, timeout, or 5xx failure while dropping the oldest entry on overflow.
 */
export class UncertainIdempotency {
  private static readonly MAX_ENTRIES = 16;
  private readonly keys = new Map<string, string>();

  take(fingerprint: string): string {
    const existing = this.keys.get(fingerprint);
    if (existing !== undefined) {
      this.keys.delete(fingerprint);
      return existing;
    }
    return newUuid();
  }

  keep(fingerprint: string, key: string): void {
    this.keys.delete(fingerprint);
    if (this.keys.size >= UncertainIdempotency.MAX_ENTRIES) {
      const oldest = this.keys.keys().next().value;
      if (oldest !== undefined) {
        this.keys.delete(oldest);
      }
    }
    this.keys.set(fingerprint, key);
  }

  clear(): void {
    this.keys.clear();
  }
}
