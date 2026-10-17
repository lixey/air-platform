import { signal } from '@angular/core';
import { groupKey } from '../app/menu-tree';
import type { MenuItemDto } from '../core/generated/contracts';

/**
 * Which groups of a menu are open: those the user opened or closed by hand, and every other by the rule its menu
 * gives — the navigation's follows the route, the mega menu's a running search.
 */
export class LayoutMenuGroups {
  private readonly touched = signal<Readonly<Record<string, boolean>>>({});

  constructor(private readonly isOpenUntouched: (module: MenuItemDto, group: MenuItemDto) => boolean) {}

  isOpen(module: MenuItemDto, group: MenuItemDto): boolean {
    return this.touched()[groupKey(module, group)] ?? this.isOpenUntouched(module, group);
  }

  /** Opens a closed group and closes an open one; from then on the group keeps what the user chose. */
  toggle(module: MenuItemDto, group: MenuItemDto): void {
    const isOpen = this.isOpen(module, group);
    this.touched.update(state => ({ ...state, [groupKey(module, group)]: !isOpen }));
  }

  /** Forgets every choice, so each group follows its menu's rule again. */
  clear(): void {
    this.touched.set({});
  }
}
