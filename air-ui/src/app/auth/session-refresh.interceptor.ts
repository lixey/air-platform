import { Location } from '@angular/common';
import type { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, type Observable, of, switchMap, throwError } from 'rxjs';
import { API_PREFIX } from '../../core/api';
import { AuthService } from './auth.service';

/**
 * While a user is signed in, a `401` of the API outside the authentication service is taken for an expired session:
 * refresh once, shared by every request that expired together. The refresh has three outcomes. It succeeds: the request
 * is retried once. The server refuses it, with a `401` or a `403`, or the retry fails with a `401` again: the session is
 * lost, the user is cleared and the browser goes to `/login`, the address as the router knows it — without the base
 * href — as its `returnUrl`. A lost session is handled once, by the first request that learns of it: every other request
 * that waited for the refused refresh, and every retry that answers `401` after the loss, is handed its own `401` and
 * neither clears the user nor navigates. It fails any other way (a `429`, a `5xx`, no answer): the session stays as it
 * is, with no retry and no navigation, and nothing repeats the refresh but the next request that answers `401`. A
 * refresh refused or failed hands the caller the `401` of its own request, never the failure of the refresh, which the
 * error alert shows once. A `401` while nobody is signed in — after a lost session, after a sign-out, before anyone
 * signed in — a `401` from the authentication service itself, a `401` from an address outside the API, which is its
 * caller's since this interceptor answers for the platform's API alone, and every other status pass through untouched:
 * no refresh and no navigation.
 */
export const sessionRefreshInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const location = inject(Location);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status !== 401 || !req.url.startsWith(API_PREFIX) || auth.isAuthUrl(req.url) || !auth.isLoggedIn()) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        // A refresh that failed without a refusal says nothing of the session: neither a retry nor a sign-out
        catchError(() => of(null)),
        switchMap(ok => {
          if (ok === null) {
            return throwError(() => error);
          }
          return ok ? retriedOnce(req, next, auth, location) : signOut(auth, location, error);
        }),
      );
    }),
  );
};

/** The request once more, after a refresh that succeeded: a second `401` means the session is gone, any other failure is the caller's. */
function retriedOnce(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  auth: AuthService,
  location: Location,
): Observable<HttpEvent<unknown>> {
  return next(req).pipe(
    catchError((again: HttpErrorResponse) => (again.status === 401 ? signOut(auth, location, again) : throwError(() => again))),
  );
}

/**
 * The session is lost: the first request to learn of it, the one that still finds a user, ends it, with the router's
 * address as the way back; a request that comes after finds nobody and ends nothing. Either is handed its own failure.
 */
function signOut(auth: AuthService, location: Location, error: HttpErrorResponse): Observable<never> {
  if (auth.isLoggedIn()) {
    auth.sessionLost(location.path() || null);
  }
  return throwError(() => error);
}
