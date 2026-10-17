/**
 * Where the text of a link cell leads: a phone to a call, an email to a letter, a site to its page. Each answers null
 * for a text it cannot lead anywhere, which the cell draws as text.
 */

/**
 * A scheme at the start of a text: letters, digits, `+` or `-` before a colon. Without a dot, so a host with a port
 * (`shop.example.com:8080`) is a site without a scheme.
 */
const SCHEME = /^([a-z][a-z0-9+-]*):/i;

/** The schemes a site may keep: a page of the web. */
const WEB_SCHEMES: readonly string[] = ['http', 'https'];

/** A `tel:` link to the `+` and the digits of the text; null for a text without digits. */
export function phoneHref(text: string): string | null {
  const number = text.replace(/[^\d+]/g, '');
  return /\d/.test(number) ? `tel:${number}` : null;
}

/**
 * A `mailto:` link to the text, percent-encoded but for its `@`, so a `?`, `&` or `,` in it adds no subject, body or
 * recipient; null for a text without `@`.
 */
export function emailHref(text: string): string | null {
  return text.includes('@') ? `mailto:${encodeURIComponent(text).replaceAll('%40', '@')}` : null;
}

/**
 * The address of a site: the text as it is when it names `http` or `https`, `https://` before it when it names no
 * scheme; null for any other scheme (`javascript:`, `mailto:`) and for what is no address even so.
 */
export function siteHref(text: string): string | null {
  const namesScheme = SCHEME.test(text);
  if (namesScheme && !isWebAddress(text)) {
    return null;
  }
  const address = namesScheme ? text : `https://${text}`;
  return URL.canParse(address) ? address : null;
}

/** Whether a link leads to a page of the web: it names `http` or `https`, in any case, as its scheme. */
export function isWebAddress(href: string): boolean {
  const scheme = SCHEME.exec(href)?.[1].toLowerCase();
  return scheme !== undefined && WEB_SCHEMES.includes(scheme);
}
