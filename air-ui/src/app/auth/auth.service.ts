import { HttpErrorResponse } from '@angular/common/http';
import { computed, DestroyRef, inject, Service, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  catchError,
  defaultIfEmpty,
  finalize,
  map,
  Observable,
  of,
  shareReplay,
  Subject,
  switchMap,
  takeUntil,
  tap,
  throwError,
} from 'rxjs';

import { Api, API_PREFIX, type ApiService } from '../../core/api';
import type { ChangePasswordRequest, LoginScreenDto, SessionDto, UserProfileDto } from '../../core/generated/contracts';
import { shownByCaller } from '../alert/error-alert.interceptor';

/** The platform's authentication service, whose own `401`s are answers, not expiries. */
const AUTH: ApiService = 'auth';

/** The browser channel the tabs of the application tell each other a sign-out on: a persistent name. */
export const SESSION_CHANNEL = 'ui:session';

/** What a tab posts on the session channel once the server has signed it out. */
const SIGNED_OUT = 'signed-out';

/**
 * Whether a failure of `auth/refresh` is the server's refusal of the session: a `401` or a `403`. Every other failure,
 * an error that is no HTTP answer among them, says nothing of the session.
 */
function isRefusal(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403);
}

/**
 * Who the user is, as anything that must notice a change of identity reads it: the id and the
 * claims, in a stable order. Whatever else the profile carries is none of its business.
 */
export function identityOf(user: UserProfileDto | null | undefined): string {
  return JSON.stringify([user?.id, Object.entries(user?.claims ?? {}).sort(([left], [right]) => left.localeCompare(right))]);
}

/**
 * The session as the browser knows it: the profile the server returned, and nothing more.
 *
 * The session itself lives in cookies the page cannot read. The service logs in and out
 * through the server, restores the profile on startup, and never holds or exposes a token. A sign-out in one tab signs
 * out every tab of the browser, through the channel `ui:session`, where the browser has one. Whatever ends the session
 * on this client ends the refresh in flight with it.
 */
@Service()
export class AuthService {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute, { optional: true });

  private readonly _currentUser = signal<UserProfileDto | null>(null);
  readonly currentUser = this._currentUser.asReadonly();
  readonly isLoggedIn = computed(() => this.currentUser() !== null);
  private readonly channel = typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(SESSION_CHANNEL);
  private refreshing: Observable<boolean> | null = null;
  /** Fires when the session ends on this client: the refresh in flight ends with it. */
  private readonly ended = new Subject<void>();

  constructor() {
    // Another tab signed out: this one's session is gone too, and it leaves as a lost session does
    this.channel?.addEventListener('message', event => {
      if (event.data === SIGNED_OUT && this.currentUser() !== null) {
        this.sessionLost();
      }
    });
    inject(DestroyRef).onDestroy(() => this.channel?.close());
  }

  /**
   * The model of the sign-in and change-password pages, in the request's culture: every word they show, before
   * anyone is signed in or while the password must change. A failure raises no global alert: the caller shows it.
   */
  loginScreen(): Observable<LoginScreenDto> {
    return this.api.post('auth/get-login-screen', undefined, shownByCaller());
  }

  /** Signs in and keeps the profile the server answers with. A failure raises no global alert: the caller shows it. */
  login(login: string, password: string): Observable<SessionDto> {
    return this.api.post('auth/login', { login, password }, shownByCaller()).pipe(tap(session => this._currentUser.set(session.user)));
  }

  /**
   * Replaces the signed-in user's password; the server answers with a new session, whose profile this service keeps: the
   * cookie transport answers the session, never the bearer's tokens. A failure raises no global alert: the caller shows
   * it, the password policy's message among others.
   */
  changePassword(currentPassword: string, newPassword: string): Observable<SessionDto> {
    const request: ChangePasswordRequest = { currentPassword, newPassword };
    return this.api
      .post<'auth/change-password', SessionDto>('auth/change-password', request, shownByCaller())
      .pipe(tap(session => this._currentUser.set(session.user)));
  }

  /**
   * Asks the server who is signed in at start-up; if the access token has expired (401), attempts to refresh the session
   * before giving up. It never fails: no profile is known yet, so any failure, of the profile or of the refresh, leaves
   * nobody signed in and answers `null`: a refresh the server refused, whose profile this member forgets itself since
   * `refresh()` leaves it, and a refresh the server did not refuse alike.
   */
  restore(): Observable<UserProfileDto | null> {
    return this.api.post('auth/get-profile', undefined).pipe(
      tap(user => this._currentUser.set(user)),
      catchError((error: HttpErrorResponse) => {
        if (error.status === 401) {
          return this.refresh().pipe(
            switchMap(ok => (ok ? of(this.currentUser()) : this.nobody())),
            catchError(() => this.nobody()),
          );
        }
        return this.nobody();
      }),
    );
  }

  /**
   * One refresh of the cookie session, shared by every caller while it is in flight, its failure included. It has three
   * outcomes: `true` when the server answered a session, whose profile is kept; `false` when the server refused the
   * refresh, with a `401` or a `403`: the session is lost, and the profile stays for the caller to end, since whoever
   * acts on the answer must still find the user it was lost for (the interceptor calls `sessionLost` while a user is
   * still signed in, `restore()` forgets the profile; any other caller calls `sessionLost` itself, while a user is still
   * signed in); and an error, the failure of `auth/refresh` as it came, for anything else (a `429`, a `5xx`, no answer):
   * nothing is known of the session, and the profile stays. A failed refresh is not repeated: the next call posts a new
   * one. A refresh in flight when the session ends on this client (`logout`, `sessionLost`, `setUser(null)`) is
   * cancelled: nothing it answers is read, it answers `false` with the profile already forgotten, and the next call
   * posts a new one.
   */
  refresh(): Observable<boolean> {
    return (this.refreshing ??= this.api.post<'auth/refresh', SessionDto>('auth/refresh', null).pipe(
      takeUntil(this.ended),
      tap(session => this._currentUser.set(session.user)),
      map(() => true),
      defaultIfEmpty(false),
      catchError((error: unknown) => (isRefusal(error) ? of(false) : throwError(() => error))),
      finalize(() => {
        this.refreshing = null;
      }),
      shareReplay(1),
    ));
  }

  /**
   * Tells the server first, then forgets the profile and ends the refresh in flight, tells the other tabs, then goes to
   * the login page: its redirect, with no way back, is the last one.
   */
  logout(): void {
    this.api
      .post('auth/logout', undefined)
      .pipe(catchError(() => of(null)))
      .subscribe(() => {
        this.end();
        this.channel?.postMessage(SIGNED_OUT);
        void this.redirectToLogin();
      });
  }

  /** The session is gone: forget the profile, end the refresh in flight and go to sign in, keeping the way back. */
  sessionLost(returnUrl: string | null = null): void {
    this.end();
    void this.redirectToLogin(returnUrl);
  }

  /**
   * The profile a flow outside this service established or ended; a lost session has its own verb, `sessionLost`. An
   * ended profile (`null`) ends the refresh in flight with it; a profile leaves it running.
   */
  setUser(user: UserProfileDto | null): void {
    if (user === null) {
      this.end();
    } else {
      this._currentUser.set(user);
    }
  }

  redirectToLogin(returnUrl: string | null = null): Promise<boolean> {
    if (returnUrl === null && this.route?.snapshot) {
      returnUrl = this.route.snapshot.queryParams['returnUrl'] || null;
    }
    if (returnUrl) {
      return this.router.navigate(['/login'], { queryParams: { returnUrl } });
    }
    return this.router.navigate(['/login']);
  }

  /** Whether a URL belongs to the authentication service, whose own `401`s are answers, not expiries. */
  isAuthUrl(url: string): boolean {
    return url.startsWith(`${API_PREFIX}${AUTH}/`);
  }

  /** Nobody is signed in: the profile is forgotten, and the answer is `null`. */
  private nobody(): Observable<null> {
    this._currentUser.set(null);
    return of(null);
  }

  /** Forgets the profile and ends the refresh in flight: whoever waited for it is answered `false`. */
  private end(): void {
    this._currentUser.set(null);
    this.ended.next();
  }
}
