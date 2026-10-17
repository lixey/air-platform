import { Location } from '@angular/common';
import { computed, effect, inject, Service, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { catchError, filter, firstValueFrom, map, Observable, of, switchMap } from 'rxjs';
import { Api } from '../core/api';
import type { AppMetadataDto, MenuItemDto, SwitcherDto, UserProfileDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { AuthService, identityOf } from './auth/auth.service';
import { findMenuItemIn, findRouteIn, trimSlash } from './menu-tree';

/**
 * The identity the metadata follows: who the user is (`identityOf`) followed by their permissions in sorted order, so
 * that a change of rights is a change of identity. Null while nobody is signed in or the user must change the
 * password: such a user gets no metadata.
 */
function metadataIdentityOf(user: UserProfileDto | null): string | null {
  if (!user || user.mustChangePassword) {
    return null;
  }
  return `${identityOf(user)}:${[...(user.permissions ?? [])].sort().join(',')}`;
}

@Service()
export class AppService {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly texts = inject(TextsService);

  private readonly _isLoaded = signal(false);
  readonly isLoaded = this._isLoaded.asReadonly();
  /** The address as the router knows it, without the base href: its location at startup, then every completed navigation. */
  readonly currentRoute = toSignal(
    inject(Router).events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(event => event.urlAfterRedirects || event.url),
    ),
    { initialValue: inject(Location).path() },
  );
  /** The current address without its query and its leading slash: what the menu compares an entry's path with. */
  readonly currentPath = computed(() => trimSlash(this.currentRoute().split('?')[0]));

  /** Every leaf the principal may view, flat, hidden ones included: what a URL resolves against. */
  private readonly _routes = signal<MenuItemDto[]>([]);
  readonly routes = this._routes.asReadonly();

  /** The visible leaves with their groups under the module nodes: what the navigation draws. */
  private readonly _menu = signal<MenuItemDto[]>([]);
  readonly menu = this._menu.asReadonly();

  /**
   * The session's switchers as the metadata carries them: the tenant's, null when the application
   * has none, and the language's. Options come only with the right to switch; the layout draws both.
   */
  private readonly _tenant = signal<SwitcherDto | null>(null);
  readonly tenant = this._tenant.asReadonly();
  private readonly _language = signal<SwitcherDto | null>(null);
  readonly language = this._language.asReadonly();
  /** The platform's configured default currency, or null when none is configured. */
  private readonly _defaultCurrency = signal<string | null>(null);
  readonly defaultCurrency = this._defaultCurrency.asReadonly();

  /** The metadata identity (`metadataIdentityOf`) the signals hold or are asked for; null while nobody gets metadata. */
  private currentIdentity: string | null = null;

  constructor() {
    // A user who signs in after startup gets the menu without a page reload; a change of claims or rights refreshes it; a logout purges it.
    effect(() => {
      const key = metadataIdentityOf(this.auth.currentUser());
      if (key === this.currentIdentity) {
        return;
      }
      this.currentIdentity = key;
      if (key === null) {
        this.hold(null);
        return;
      }
      // The error alert has shown a failed answer; the navigation stays as it was.
      untracked(() => this.loadFor(key).subscribe({ error: () => undefined }));
    });
  }

  /**
   * Startup: ask the server who is signed in, then, for a signed-in user, the metadata.
   * A signed-out user gets no metadata call; the routes and the guard send them to `/login`.
   * No route is registered here: the application's own routes mount the dynamic layout,
   * which resolves its entry by URL.
   */
  async init(): Promise<void> {
    await firstValueFrom(
      this.auth.restore().pipe(
        switchMap(user => {
          const key = metadataIdentityOf(user);
          if (key === null) {
            return of(undefined);
          }
          this.currentIdentity = key;
          return this.loadFor(key);
        }),
        // The error alert has shown a failed answer; the application starts without the metadata.
        catchError(() => of(undefined)),
      ),
      { defaultValue: undefined },
    );
    this._isLoaded.set(true);
  }

  currentModule(): MenuItemDto {
    const segments = this.currentPath().split('/');
    const currentModule = this.menu().find(m => m.path === segments[0]);
    return currentModule ?? this.menu()[0] ?? { isSeparator: false, items: [] };
  }

  /** The entry a URL names: a published route first, hidden or not, then a menu node such as a module or a group. */
  findMenuItem(url: string): MenuItemDto | null {
    return findRouteIn(this.routes(), url) ?? findMenuItemIn(this.menu(), url);
  }

  /** Asks for the metadata of the identity `key` and holds the answer only while that identity is still current. */
  private loadFor(key: string): Observable<void> {
    return this.api.post('metadata/get-data', undefined).pipe(
      map(data => {
        if (this.currentIdentity === key) {
          this.hold(data);
        }
      }),
    );
  }

  /** Holds both projections and the switchers as the server published them; null purges them. */
  private hold(data: AppMetadataDto | null): void {
    this._routes.set(data?.routes ?? []);
    this._menu.set(data?.menu ?? []);
    this._tenant.set(data?.tenant ?? null);
    this._language.set(data?.language ?? null);
    this._defaultCurrency.set(data?.defaultCurrency ?? null);
    this.texts.setTexts(data?.texts ?? {});
  }
}
