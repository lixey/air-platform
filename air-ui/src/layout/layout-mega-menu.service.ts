import { computed, inject, type Injector, Service, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { AppService } from '../app/app.service';
import { modulePathOf, redirectFor } from '../app/menu-tree';
import type { MenuItemDto, OverlayModelDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { OverlayService } from '../overlay/overlay.service';
import { PANELS } from '../panels/panels-tokens';
import { LayoutMenuGroups } from './layout-menu-groups';
import { MENU_PANEL } from './layout-panel-keys';

/**
 * The tree, keeping only what a name search matches.
 *
 * An entry whose own name matches keeps everything under it, so searching for a group finds
 * the group whole. Otherwise an entry survives because something below it did, which is what
 * makes a match inside a folded group reachable without opening every group first.
 */
export function filterMenuTree(items: readonly MenuItemDto[], query: string): MenuItemDto[] {
  const wanted = query.trim().toLowerCase();

  if (!wanted) {
    return [...items];
  }

  const walk = (list: readonly MenuItemDto[]): MenuItemDto[] => {
    const kept: MenuItemDto[] = [];

    for (const item of list) {
      if (item.isSeparator) {
        continue;
      }

      const matches = (item.name ?? '').toLowerCase().includes(wanted);

      if (matches) {
        kept.push(item);
        continue;
      }

      const children = walk(item.items ?? []);

      if (children.length) {
        kept.push({ ...item, items: children });
      }
    }

    return kept;
  };

  return walk(items);
}

/**
 * The mega menu as an overlay: a popover that `Escape` and a click outside close, 1024 pixels wide and as tall as what
 * it shows. It states no height: its one panel, the body, is sized to its content, so the card grows as a group opens
 * and is measured and placed again by the overlay host, which keeps it inside the window's frame. Only a tree taller
 * than the frame is clamped to it, and then the tree scrolls inside the body while its filter stays in sight.
 */
const MEGA_MENU: OverlayModelDto = {
  key: 'layout.mega-menu',
  presentation: 'popover',
  closesOnEscape: true,
  closesOnOutside: true,
  width: '1024px',
  layout: {
    key: 'app.mega-menu',
    root: {
      key: 'root',
      direction: 'column',
      children: [{ key: 'body', model: { kind: 'LayoutMegaMenuBody' }, size: 'auto' }],
    },
  },
};

/**
 * The mega menu: the whole tree at once, as the way to the menu while the navigation is hidden.
 *
 * It opens as a popover of the overlay service, anchored to its trigger and named `Navigation`, so the host traps its
 * focus, closes it on `Escape` and on a click outside, and hands focus back to the trigger. It is a part of the frame
 * that floats, so it opens in the frame's scale and reads the brand, as the navigation does. The service keeps what the
 * overlay shows — the filter and the groups the user opened — because the overlay's parts are panels: the engine
 * mounts them through the component registry, which hands a component its model and nothing else, so the body reaches
 * the filter and the tree through this service. Provided by the strip that opens the overlay, which passes its
 * injector down to the body.
 */
@Service({ autoProvided: false })
export class LayoutMegaMenuService {
  private readonly app = inject(AppService);
  private readonly router = inject(Router);
  private readonly overlay = inject(OverlayService);
  private readonly texts = inject(TextsService);
  private readonly panels = inject(PANELS, { optional: true });

  /** The overlay's key: what navigating and restoring the navigation close, and what the trigger says of the card. */
  private readonly menu = this.overlay.menu();

  private readonly _searchText = signal('');
  readonly searchText = this._searchText.asReadonly();

  /** Whether the menu is open: what its trigger's `aria-expanded` says. */
  readonly isOpen = this.menu.isOpen;

  /** The id of the open card, `null` while the menu is closed: what its trigger's `aria-controls` names. */
  readonly id = this.menu.id;

  readonly modules = computed(() => filterMenuTree(this.app.menu(), this.searchText()));

  /**
   * Whether the navigation can be brought back at all: the frame's controls model lists it,
   * which below `lg` it does not. The overlay asks the model rather than the window, so the
   * two can never disagree.
   */
  readonly canRestoreNav = computed(() => this.panels?.root.togglePanels().some(t => t.path === MENU_PANEL) ?? false);

  readonly modulePathOf = modulePathOf;

  /**
   * A group opens when the user says so, and otherwise only while a search is running: a group that survived the
   * filter holds a match.
   */
  private readonly groups = new LayoutMenuGroups(() => this.searchText().trim().length > 0);

  constructor() {
    // Whatever navigates — an entry of the menu, a tab, the browser's history — leaves the menu behind
    this.router.events
      .pipe(
        filter(event => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.close());
  }

  /**
   * Opens the menu from its trigger, with an empty filter and every group folded; the injector is the strip's, through
   * which the body reaches this service, and the trigger stands in the frame, so the card reads the brand. An open menu
   * stays as it is: the frame beneath it is not inert, so a screen reader can still reach the trigger.
   */
  open(trigger: HTMLElement, injector: Injector): void {
    if (this.menu.isOpen()) {
      return;
    }
    this._searchText.set('');
    this.groups.clear();

    this.menu.open(MEGA_MENU, trigger, injector, this.texts.of('Navigation'));
  }

  /** Closes the menu; the overlay host hands focus back to the trigger, if it is still in the document. */
  close(): void {
    this.menu.close();
  }

  /**
   * What the search box holds: the tree narrows to it. A search forgets the groups folded or opened by hand, so every
   * group holding a match opens.
   */
  search(text: string): void {
    this._searchText.set(text);
    this.groups.clear();
  }

  /** Brings the navigation back, closing the menu first, since the navigation is then the way to the menu. */
  restoreNav(): void {
    this.close();
    this.panels?.open(MENU_PANEL);
  }

  isGroupOpen(module: MenuItemDto, group: MenuItemDto): boolean {
    return this.groups.isOpen(module, group);
  }

  toggleGroup(module: MenuItemDto, group: MenuItemDto): void {
    this.groups.toggle(module, group);
  }

  /** A module header opens the module's first leaf and closes the overlay. */
  openModule(module: MenuItemDto): void {
    const target = redirectFor(module);

    if (target) {
      void this.router.navigateByUrl(target);
      this.close();
    }
  }
}
