import { booleanAttribute, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppService } from '../app/app.service';
import { menuItemPath } from '../app/menu-tree';
import type { MenuItemDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { FOCUS_RING } from '../theme/css-classes';
import { LayoutPinsService } from './layout-pins.service';

/**
 * One entry of a menu: the row the navigation and the mega menu both draw, so an entry the user has learned in one
 * place is the same entry in the other.
 *
 * The row is a link to the entry's address, marked `aria-current="page"` while the route is that address. Entries
 * inside a group are indented no further than any other: the rule down the left of the row is what says where they
 * belong, and it costs two pixels where an indent would cost twelve.
 *
 * The mark that says an entry is pinned and the control that pins it share one slot at the right edge, drawn over the
 * end of the label rather than beside it — so a row at rest spends none of its width on them. The label is never
 * hidden: it wraps to two lines and then ellipsizes.
 */
@Component({
  selector: 'ui-layout-menu-entry',
  imports: [RouterLink, UiIconDirective],
  template: `
    <div
      class="group/row relative flex cursor-pointer items-stretch bg-surface hover:bg-active hover:text-on-active"
      [class]="rowClasses()"
    >
      <a
        [routerLink]="'/' + path()"
        [attr.aria-current]="isCurrent() ? 'page' : null"
        [attr.title]="leaf().name"
        class="flex min-w-0 flex-1 items-center py-1.5 pl-2"
        [class.pr-2]="!isPinned()"
        [class.pr-6]="isPinned()"
        (click)="opened.emit()"
      >
        <span class="min-w-0 flex-1 leading-tight line-clamp-2 break-words">{{ leaf().name }}</span>
      </a>

      <!--
        The mark, and the way to undo it, are one target. A pinned row therefore offers nothing new under the pointer,
        and the room the mark occupies is taken from the label rather than laid over it, so a click near the end of a
        name cannot unpin by accident. One button whose name says what it does next, so it carries no aria-pressed,
        and pinning keeps the focus where it was.
      -->
      <button
        type="button"
        [class]="pinClasses()"
        [attr.aria-label]="pinLabel()"
        [attr.tabindex]="!isPinned() && !pins.altPressed() ? -1 : null"
        [title]="pinTitle()"
        (click)="togglePin($event)"
      >
        @if (isPinned()) {
          <span class="h-1.5 w-1.5 rounded-full bg-current"></span>
        } @else {
          <i [icon]="'plus'"></i>
        }
      </button>
    </div>
  `,
  host: {
    class: 'block',
  },
})
export class LayoutMenuEntryComponent {
  private readonly texts = inject(TextsService);
  private readonly app = inject(AppService);
  protected readonly pins = inject(LayoutPinsService);

  /** The entry the row draws: a leaf of the menu, whose path may be relative to its module. */
  readonly leaf = input.required<MenuItemDto>();
  /** The address of the module the entry sits in, which a relative path is joined to. */
  readonly modulePath = input.required<string>();
  /** Whether the entry sits inside a group, which draws the rule down its left. */
  readonly nested = input(false, { transform: booleanAttribute });

  /** Emitted when the user follows the entry's link, so a menu overlay can close. */
  readonly opened = output<void>();

  protected readonly path = computed(() => menuItemPath(this.modulePath(), this.leaf()));
  protected readonly isPinned = computed(() => this.pins.isPinned(this.path()));
  protected readonly isCurrent = computed(() => {
    const current = this.app.currentPath().toLowerCase();
    const entryPath = this.path().toLowerCase();
    return current === entryPath || current.startsWith(`${entryPath}/`);
  });

  /** The rule of a grouped entry, and the ground of the current one over the hover. */
  protected readonly rowClasses = computed(() =>
    [this.nested() ? 'border-l border-line' : '', this.isCurrent() ? 'bg-active! text-on-active' : ''].join(' '),
  );

  /** What the pin does next, named with the entry, and the short form its tooltip shows. */
  protected readonly pinLabel = computed(() => this.texts.of(this.isPinned() ? 'UnpinEntry' : 'PinEntry', this.leaf().name ?? ''));
  protected readonly pinTitle = computed(() => this.texts.of(this.isPinned() ? 'Unpin' : 'Pin'));

  /** The pin at rest: a pinned row shows its mark, an unpinned one only under the pointer while Alt/Option is held. */
  protected readonly pinClasses = computed(() =>
    [
      `absolute inset-y-0 right-0 flex w-7 cursor-pointer items-center justify-center transition-opacity ${FOCUS_RING}`,
      this.isPinned()
        ? 'opacity-40 hover:opacity-100 focus-visible:opacity-100'
        : this.pins.altPressed()
          ? 'bg-inherit opacity-0 group-hover/row:opacity-60 hover:opacity-100 focus-visible:opacity-100'
          : 'pointer-events-none opacity-0',
    ].join(' '),
  );

  protected togglePin(event?: MouseEvent): void {
    event?.preventDefault();
    event?.stopPropagation();
    const path = this.path();

    if (path) {
      this.pins.toggle({ ...this.leaf(), path });
    }
  }
}
