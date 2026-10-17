import { type HttpContext, HttpContextToken, type HttpInterceptorFn } from '@angular/common/http';
import { API_PREFIX } from '../../core/api';
import { browserZone } from '../../core/time-zone';
import { shownByCaller } from '../alert/error-alert.interceptor';

/** The header a cookie-authenticated request must carry; the server refuses one without it. */
export const PLATFORM_CLIENT_HEADER = 'X-Platform-Client';

/**
 * The header naming the zone the browser stands in: a fact of the device, whatever the profile says. The server reads
 * it last but one in the chain of the request's zone, after every zone it stores, and grants nothing by it.
 */
export const PLATFORM_TIME_ZONE_HEADER = 'X-Platform-Time-Zone';

/**
 * The header of a create's idempotency key: the server keeps the answer of the first committed run under it and answers
 * a repeat with that answer, so a create whose answer was lost is made once however often it is sent again.
 */
export const PLATFORM_IDEMPOTENCY_KEY_HEADER = 'Idempotency-Key';

/** The idempotency key a request's caller names in its context; none by default. */
export const IDEMPOTENCY_KEY = new HttpContextToken<string | null>(() => null);

/**
 * The options of a request posted under the idempotency key `key`: `options` with the key set on their context, or
 * those of `shownByCaller()` when none are given, since a create's caller shows its own failure. The caller makes the
 * key once per create, `crypto.randomUUID()`, and keeps it while the create's outcome is unknown.
 */
export function withIdempotencyKey(key: string, options?: { context: HttpContext }): { context: HttpContext } {
  const keyed = options ?? shownByCaller();
  keyed.context.set(IDEMPOTENCY_KEY, key);
  return keyed;
}

/**
 * Every API request travels with credentials, the client header and the browser's zone, and a request whose context
 * names an idempotency key with that key; no `Authorization` header is composed here.
 */
export const credentialsInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_PREFIX)) {
    return next(req);
  }
  const key = req.context.get(IDEMPOTENCY_KEY);
  return next(
    req.clone({
      withCredentials: true,
      setHeaders: {
        [PLATFORM_CLIENT_HEADER]: 'browser',
        [PLATFORM_TIME_ZONE_HEADER]: browserZone(),
        ...(key === null ? {} : { [PLATFORM_IDEMPOTENCY_KEY_HEADER]: key }),
      },
    }),
  );
};
