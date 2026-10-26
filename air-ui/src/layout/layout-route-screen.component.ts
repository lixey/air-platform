import { Component, computed, effect, ErrorHandler, inject, linkedSignal } from '@angular/core';
import { ActivatedRoute, Router, RouterOutlet } from '@angular/router';
import { AppService } from '../app/app.service';
import { redirectFor, rootRedirect } from '../app/menu-tree';
import { BtnComponent } from '../btn/btn.component';
import type { MenuItemDto, ScreenDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { PanelsComponent } from '../panels/panels.component';
import { SCREEN_HOST, type ScreenHost } from '../screen/screen-address';
import { ScreenLifecycle } from '../screen/screen-lifecycle';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { screenLoader } from '../screen/screen-loader';
import { ScreenMutations } from '../screen/screen-mutations';
import { WORK_AREA } from '../theme/css-classes';

/**
 * What one route renders: the screen its navigation entry names. This asks the server how the screen is arranged,
 * and `<ui-panels>` draws it, arrangement and first data in a single request. A route that only groups others
 * renders the outlet its children go into.
 */
@Component({
  selector: 'ui-layout-route-screen',
  imports: [BtnComponent, PanelsComponent, RouterOutlet],
  providers: [
    ScreenLifecycle,
    { provide: SCREEN_LIFETIME, useFactory: () => inject(ScreenLifecycle).lifetime },
    { provide: ScreenMutations, useFactory: () => inject(ScreenLifecycle).mutations },
    { provide: SCREEN_HOST, useExisting: LayoutRouteScreenComponent },
  ],
  template: `
    @if (hasChildRoute()) {
      <router-outlet />
    } @else if (screen.error()) {
      <div role="alert" class="m-3 p-4 flex items-center gap-3 text-sm text-warning">
        {{ texts.of('ScreenLoadFailed') }}
        <ui-btn [label]="texts.of('Retry')" (click)="screen.reload()" />
      </div>
    } @else if (screen.layout(); as layout) {
      <ui-panels [layout]="layout" [data]="screen.data()" />
    } @else if (!screen.isLoading()) {
      <router-outlet />
    }
  `,
  host: {
    // `ui-work`: what the outlet draws is the work area, and reads the slate roles whatever the brand
    class: `${WORK_AREA} flex flex-col grow shrink overflow-hidden min-h-0 min-w-0 w-full h-full @container @container/outlet`,
  },
})
export class LayoutRouteScreenComponent implements ScreenHost {
  readonly lifecycle = inject(ScreenLifecycle);
  protected readonly texts = inject(TextsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly app = inject(AppService);
  private readonly errorHandler = inject(ErrorHandler);

  /** The parameters a panel asked to load the screen with; another menu entry starts without them. */
  private readonly patch = linkedSignal<MenuItemDto | null, Record<string, unknown>>({
    source: () => this.activeItem(),
    computation: () => ({}),
  });

  /**
   * A module or a group address opens its first leaf, and the empty path the first leaf of the
   * visible menu; nothing is registered for that, the entry says where.
   */
  private readonly redirect = effect(() => {
    if (this.hasChildRoute()) {
      return;
    }
    const item = this.activeItem();
    // The root case waits for the router's first navigation: an application's own root route goes first
    const atRoot = this.router.navigated && this.app.currentPath() === '';
    let target: string | null = null;
    if (item) {
      target = redirectFor(item);
    } else if (atRoot) {
      target = rootRedirect(this.app.menu());
    }
    if (target) {
      this.router.navigateByUrl(target).catch((error: unknown) => this.errorHandler.handleError(error));
    }
  });

  /** The entry the current URL resolves to in the metadata, which is what says who renders it; a route carries no data of its own. */
  readonly activeItem = computed<MenuItemDto | null>(() => {
    const url = this.app.currentRoute();
    return url ? this.app.findMenuItem(url) : null;
  });

  /** A route with children renders them; only a leaf resolves a screen of its own. */
  protected readonly hasChildRoute = computed(() => {
    this.app.currentRoute();
    return this.route.firstChild !== null;
  });

  /** The screen the entry names, loaded by the same loader an overlay uses. */
  protected readonly screen = screenLoader(() => {
    const screen = this.hasChildRoute() ? null : this.screenOf(this.activeItem());
    const patch = this.patch();
    const params = Object.keys(patch).length > 0 ? patch : undefined;
    return screen && this.lifecycle.isActive() ? { service: screen, params } : null;
  });

  reload(patch: Record<string, unknown>): void {
    this.patch.update(current => ({ ...current, ...patch }));
  }

  show(screen: ScreenDto): void {
    this.screen.show(screen);
  }

  /** The screen a menu entry names. */
  private screenOf(item: MenuItemDto | null): string | null | undefined {
    return item?.screen;
  }
}
