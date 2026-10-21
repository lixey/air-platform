import type { MenuItemDto } from '../core/generated/contracts';

/** A path or an address without its leading slash: how the menu compares and joins them. */
export function trimSlash(path: string | null | undefined): string {
  return (path ?? '').replace(/^\//, '');
}

/** The address an entry opens at, from the root; null for an entry without a path. */
function addressOf(item: MenuItemDto | undefined): string | null {
  return item?.path ? `/${trimSlash(item.path)}` : null;
}

/**
 * Puts a node's own path after the path of everything above it.
 *
 * An entry with no path of its own contributes no segment, which is what lets a group sit
 * in the tree without appearing in an address. An entry that already carries the whole
 * path is left alone, so metadata may state either form.
 */
export function joinMenuPath(parentPath: string, path?: string | null): string {
  const own = trimSlash(path);

  if (!own) {
    return parentPath;
  }

  if (!parentPath || own === parentPath || own.startsWith(`${parentPath}/`)) {
    return own;
  }

  return `${parentPath}/${own}`;
}

/**
 * The menu entry a URL resolves to, at any depth.
 *
 * The address is built while walking down, so a group without a path of its own adds
 * nothing to it and its children answer to the address their module gives them.
 */
export function findMenuItemIn(menu: readonly MenuItemDto[], url: string): MenuItemDto | null {
  const clean = trimSlash(url.split('?')[0]).toLowerCase();
  if (!clean) {
    return null;
  }

  const search = (items: readonly MenuItemDto[], parentPath = ''): MenuItemDto | null => {
    for (const item of items) {
      const fullPath = joinMenuPath(parentPath, item.path);
      const ownPath = trimSlash(item.path).toLowerCase();

      if (ownPath && (fullPath.toLowerCase() === clean || ownPath === clean)) {
        return item;
      }

      if (item.items?.length) {
        if (fullPath && fullPath.toLowerCase() === clean && ownPath) {
          return item;
        }

        const found = search(item.items, fullPath);

        if (found) {
          return found;
        }
      }
    }

    return null;
  };

  return search(menu);
}

/**
 * The route a URL names: every leaf the principal may view, listed flat by the server,
 * the hidden ones included. A leaf found here answers to its address whether or not any
 * menu lists it.
 */
export function findRouteIn(routes: readonly MenuItemDto[], url: string): MenuItemDto | null {
  const clean = trimSlash(url.split('?')[0]).toLowerCase();
  if (!clean) {
    return null;
  }
  return routes.find(route => trimSlash(route.path).toLowerCase() === clean) ?? null;
}

/** The first entry that can actually be opened, looking through groups that have no path. */
export function firstRoutableItem(items: readonly MenuItemDto[] | undefined): MenuItemDto | undefined {
  for (const item of items ?? []) {
    if (!item || item.isSeparator) {
      continue;
    }

    if (item.path) {
      return item;
    }

    const nested = firstRoutableItem(item.items);

    if (nested) {
      return nested;
    }
  }

  return undefined;
}

/**
 * Where a module or a group address goes: to its first leaf. An entry that names a screen is a destination and
 * redirects nowhere.
 */
export function redirectFor(item: MenuItemDto | null): string | null {
  if (!item || item.screen || !item.items?.length) {
    return null;
  }

  return addressOf(firstRoutableItem(item.items));
}

/**
 * Where the empty path goes: to the first routable leaf of the visible menu, the first screen the
 * user may see. Null when the menu holds none, and then the frame stays empty.
 */
export function rootRedirect(menu: readonly MenuItemDto[]): string | null {
  return addressOf(firstRoutableItem(menu.flatMap(module => module.items ?? [])));
}

/** The address of a module in the menu: its path, or its handle where it has none. */
export function modulePathOf(module: MenuItemDto): string {
  return trimSlash(module.path || module.handle).toLowerCase();
}

/** What identifies a group under its module while the user opens and closes it. */
export function groupKey(module: MenuItemDto, group: MenuItemDto): string {
  return `${module.handle ?? ''}/${group.handle ?? group.name ?? ''}`;
}

/** A grouping node: it holds entries and leads nowhere itself. */
export function isMenuGroup(item: MenuItemDto): boolean {
  return !item.isSeparator && !item.path && (item.items?.length ?? 0) > 0;
}

/** The address a menu entry answers to, given the module it sits in. */
export function menuItemPath(modulePath: string, item: MenuItemDto | undefined): string {
  return joinMenuPath(modulePath, item?.path);
}

/** Whether a group holds the entry the given address resolves to. */
export function groupHoldsPath(modulePath: string, group: MenuItemDto, path: string): boolean {
  const target = path.toLowerCase();
  return (group.items ?? []).some(leaf => {
    if (!leaf.path) {
      return false;
    }
    const leafPath = menuItemPath(modulePath, leaf).toLowerCase();
    return leafPath === target || target.startsWith(`${leafPath}/`);
  });
}
