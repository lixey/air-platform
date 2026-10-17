import { Component, computed, inject, Injector } from '@angular/core';
import { Router } from '@angular/router';
import { AppService } from '../app/app.service';
import { trimSlash } from '../app/menu-tree';
import { BtnComponent } from '../btn/btn.component';
import type { MenuItemDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { isExternalPath } from '../core/url';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { NavComponent } from '../nav/nav.component';
import { PANELS } from '../panels/panels-tokens';
import { FOCUS_RING } from '../theme/css-classes';
import { LayoutMegaMenuService } from './layout-mega-menu.service';
import { MENU_PANEL } from './layout-panel-keys';
import { LayoutPinsService } from './layout-pins.service';
import { LayoutSignOutComponent } from './layout-sign-out.component';
import { LayoutSwitchersComponent } from './layout-switchers.component';
import { ON_PAGE } from './on-page';

/**
 * Whether the strip is worth the room it takes.
 *
 * Nothing pinned and a navigation in plain sight means there is nothing to put here, and a
 * strip drawn anyway would cost a row of height for the whole session. It earns its place
 * the moment the user pins something, or the moment it becomes the only way back to the menu.
 */
export function isTabStripVisible(pins: readonly MenuItemDto[], isNavHidden: boolean): boolean {
  return pins.length > 0 || isNavHidden;
}

/**
 * What the strip shows: the pins, and — only while the navigation is gone — where the user
 * currently is, when that is not already pinned.
 */
export function visibleTabs(pins: readonly MenuItemDto[], current: MenuItemDto | null, isNavHidden: boolean): readonly MenuItemDto[] {
  if (!isNavHidden || !current?.path) {
    return pins;
  }

  return pins.some(pin => pin.path === current.path) ? pins : [...pins, current];
}

/**
 * The strip of things the user keeps at hand, and the mega menu it can summon.
 *
 * It fills a panel of its own at the top of the work area — a panel the frame declares
 * transparent, so the strip lies on the page and a tab can be a solid chip against it. Being
 * inside the layout is also how it knows whether the navigation is in sight: it asks the
 * engine, rather than being told, so a hide by drag, by a container too narrow, or by the
 * user all read the same.
 *
 * A tab is a menu entry and nothing more, so what it takes to draw one and what it takes to
 * open one both came with the entry. An entry whose address leaves the application opens
 * outside it; every other opens through the router.
 *
 * The strip is a nav: the menu trigger fixed at its start and the session controls — the
 * tenant and language switchers, then sign-out — at its end, both only while the navigation is
 * hidden, and the tabs scrolling between them.
 * The nav pads nothing here, because the strip's panel is transparent and the engine's
 * gap around it is its inset — which is what keeps the tabs level with the navigation's card.
 */
@Component({
  selector: 'ui-layout-tabs',
  imports: [UiIconDirective, NavComponent, BtnComponent, LayoutSwitchersComponent, LayoutSignOutComponent],
  template: `
    <ui-nav overflow="scroll">
      <ng-container ngProjectAs="[start]">
        @if (isNavHidden()) {
          <ui-btn
            size="small"
            variant="ghost"
            icon="menu"
            [title]="texts.of('MenuOpen')"
            class="${ON_PAGE}"
            aria-haspopup="dialog"
            [attr.aria-expanded]="megaMenu.isOpen()"
            [attr.aria-controls]="megaMenu.id()"
            (click)="openMenu($event)"
          />
        }
      </ng-container>

      @for (tab of tabs(); track tab.path) {
        <div [class]="tabClasses(tab)">
          <button
            type="button"
            class="flex min-h-7 cursor-pointer items-center py-0.5 pl-2 pr-6 text-sm ${FOCUS_RING}"
            [attr.title]="tab.name"
            [attr.aria-current]="isActive(tab) ? 'page' : null"
            (click)="open(tab)"
            (auxclick)="onAuxClick($event, tab)"
          >
            <span class="max-w-56 truncate">{{ tab.name }}</span>
          </button>

          <!--
            The room is reserved whether or not the control is drawn, so the control never
            lands on the label and a tab never changes width as the pointer crosses it — which
            matters more in a row than in a column, because a tab that grows pushes its
            neighbours along with it.
          -->
          <button
            type="button"
            [class]="closeClasses(tab)"
            [attr.title]="texts.of('CloseTab', tab.name ?? '')"
            [attr.aria-label]="texts.of('CloseTab', tab.name ?? '')"
            (click)="close(tab)"
          >
            <!-- Smaller than the shared icon size, which the layer below sets for icons that
                 are the subject of their control rather than a way to dismiss one. -->
            <i class="[&>svg]:h-4 [&>svg]:w-4" [icon]="'close'"></i>
          </button>
        </div>
      }

      <ng-container ngProjectAs="[end]">
        @if (isNavHidden()) {
          <ui-layout-switchers onPage />
          <ui-layout-sign-out onPage />
        }
      </ng-container>
    </ui-nav>
  `,
  host: {
    class: 'block select-none text-sm',
  },
  providers: [LayoutMegaMenuService],
})
export class LayoutTabsComponent {
  protected readonly texts = inject(TextsService);
  /** Shared by every part of the mega menu this strip opens, which reach it through the strip's injector. */
  protected readonly megaMenu = inject(LayoutMegaMenuService);
  private readonly injector = inject(Injector);

  private readonly app = inject(AppService);
  private readonly router = inject(Router);
  private readonly panels = inject(PANELS, { optional: true });

  readonly pins = inject(LayoutPinsService);

  /**
   * Whether the navigation is out of sight. Asked of the engine, which is safe here because
   * this component is mounted inside the layout and so cannot run before it has one.
   */
  protected readonly isNavHidden = computed(() => this.panels?.isCollapsed(MENU_PANEL) ?? false);

  /** Where the user is, named by the menu entry that address resolves to. */
  private readonly current = computed<MenuItemDto | null>(() => {
    const path = this.app.currentPath();
    const item = path ? this.app.findMenuItem(path) : null;

    return item ? { ...item, path, items: [] } : null;
  });

  protected readonly tabs = computed(() => visibleTabs(this.pins.pins(), this.current(), this.isNavHidden()));

  protected isActive(tab: MenuItemDto): boolean {
    const current = this.app.currentPath().toLowerCase();
    const tabPath = (tab.path ?? '').toLowerCase();
    return current === tabPath || current.startsWith(`${tabPath}/`);
  }

  /**
   * An inactive tab is transparent, so it reads as sitting on the page; the active one is
   * solid, which is what tells it from the rest without a second cue. Hovering shows what
   * activating would give, and the active tab has nothing to show for it.
   *
   * The text is light in both themes rather than following one, because the ground it sits
   * on is dark in both.
   */
  protected tabClasses(tab: MenuItemDto): string {
    const base = 'group/tab relative flex shrink-0 cursor-pointer items-center rounded-xl';

    return this.isActive(tab) ? `${base} bg-active text-on-active` : `${base} text-on-active hover:bg-active`;
  }

  /** Drawn on the active tab and under the pointer; its room is held either way. */
  protected closeClasses(tab: MenuItemDto): string {
    const base = `absolute inset-y-0 right-0 flex w-6 cursor-pointer items-center justify-center rounded-r-xl transition-opacity ${FOCUS_RING}`;

    return this.isActive(tab)
      ? `${base} opacity-70 hover:opacity-100`
      : `${base} opacity-0 group-hover/tab:opacity-70 hover:opacity-100 focus-visible:opacity-100`;
  }

  protected open(tab: MenuItemDto): void {
    const path = tab.path ?? '';

    if (!path) {
      return;
    }

    if (isExternalPath(path)) {
      window.open(path, '_blank', 'noopener');
      return;
    }

    void this.router.navigateByUrl(`/${trimSlash(path)}`);
  }

  protected close(tab: MenuItemDto): void {
    this.pins.unpin(tab.path);
  }

  /** Opens the mega menu anchored to the trigger that was pressed. */
  protected openMenu(event: Event): void {
    const trigger = event.currentTarget;
    if (trigger instanceof HTMLElement) {
      this.megaMenu.open(trigger, this.injector);
    }
  }

  /** The middle button closes a tab, the way it closes one anywhere else. */
  protected onAuxClick(event: MouseEvent, tab: MenuItemDto): void {
    if (event.button === 1) {
      event.preventDefault();
      this.close(tab);
    }
  }
}
