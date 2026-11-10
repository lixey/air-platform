/**
 * Generates an RFC 4122 version 4 UUID using `crypto.randomUUID()` in secure contexts (HTTPS / localhost)
 * and falling back to `crypto.getRandomValues()` over plain HTTP LAN connections (`http://192.168.x.x`).
 */
export function newUuid(): string {
  const c = globalThis.crypto;
  if (typeof c?.randomUUID === 'function') {
    return c.randomUUID();
  }

  const bytes = new Uint8Array(16);
  c.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0'));
  return `${hex[0]}${hex[1]}${hex[2]}${hex[3]}-${hex[4]}${hex[5]}-${hex[6]}${hex[7]}-${hex[8]}${hex[9]}-${hex[10]}${hex[11]}${hex[12]}${hex[13]}${hex[14]}${hex[15]}`;
}
