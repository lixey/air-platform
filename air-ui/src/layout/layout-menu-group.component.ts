import { Component, input, output } from '@angular/core';
import type { MenuItemDto } from '../core/generated/contracts';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { LayoutMenuEntryComponent } from './layout-menu-entry.component';

/**
 * A menu group, as the navigation and the mega menu both draw it: its header and, while the group is open, the list of
 * its entries. The header is the group's band and a chevron, told from a module's header by weight alone. It discloses
 * what the group holds; whether the group is open is its menu's to say, and activating the header asks the menu to
 * toggle it. The entries are indented one step, with the rule beside them rather than around the header as well.
 *
 * The host makes no box: the header and the list are items of the column the menu stands the group in.
 *
 * The name wraps to two lines and then ellipsizes, as an entry's does: a name the user cannot read is a group they
 * cannot find.
 */
@Component({
  selector: 'ui-layout-menu-group',
  imports: [UiIconDirective, LayoutMenuEntryComponent],
  template: `
    <button
      type="button"
      class="flex w-full cursor-pointer items-center gap-2 bg-surface-2 py-1.5 pl-2 text-left hover:bg-active hover:text-on-active"
      [attr.title]="group().name"
      [attr.aria-expanded]="open()"
      (click)="toggled.emit()"
    >
      <span class="min-w-0 flex-1 leading-tight line-clamp-2 break-words">{{ group().name }}</span>
      <i class="flex w-7 shrink-0 items-center justify-center opacity-60" [icon]="open() ? 'chevron-bottom' : 'chevron-right'"></i>
    </button>

    @if (open()) {
      <div class="flex flex-col bg-surface pl-2">
        @for (leaf of group().items; track leaf.handle ?? $index) {
          <ui-layout-menu-entry nested [leaf]="leaf" [modulePath]="modulePath()" (opened)="opened.emit()" />
        }
      </div>
    }
  `,
  host: {
    class: 'contents',
  },
})
export class LayoutMenuGroupComponent {
  /** The group the header names and the list holds the entries of. */
  readonly group = input.required<MenuItemDto>();
  /** The path of the module the group stands in, which its entries compose their addresses with. */
  readonly modulePath = input.required<string>();
  /** Whether the group's entries are showing, which the chevron and `aria-expanded` say. */
  readonly open = input.required<boolean>();

  /** Emitted when the user activates the header, for the menu to open or close the group. */
  readonly toggled = output<void>();
  /** Emitted when the user follows an entry of the group. */
  readonly opened = output<void>();
}
