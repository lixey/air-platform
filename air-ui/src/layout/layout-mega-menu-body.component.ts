import { Component, inject } from '@angular/core';
import { isMenuGroup } from '../app/menu-tree';
import { BtnComponent } from '../btn/btn.component';
import { SearchBoxComponent } from '../controls/search-box.component';
import { TextsService } from '../core/texts.service';
import { NavComponent } from '../nav/nav.component';
import { LayoutMegaMenuService } from './layout-mega-menu.service';
import { LayoutMenuEntryComponent } from './layout-menu-entry.component';
import { LayoutMenuGroupComponent } from './layout-menu-group.component';
import { LayoutSwitchersComponent } from './layout-switchers.component';
import { LayoutThemeButtonComponent } from './layout-theme-button.component';

/**
 * The menu overlay's one panel: what to search for, and everything there is to find.
 *
 * It is spaced by the gap the engine puts between panels, read from that gap rather than
 * from a utility step that matches it today: the root font size changes with the window, so
 * a rem-based step would agree with the panels at one width and not at another.
 *
 * The tree here is the one the navigation draws — the same grounds for a module, a group and
 * an entry, the same text tone, the same hover, the same hairline under a third level. An
 * entry the user has learned in one place is the same entry in the other, and a block adds no
 * padding of its own: what separates its parts is their ground.
 *
 * The nav is the frame's own, one row of medium controls: the library's search field, as short as a grid's, which
 * narrows the tree as the user types; the tenant and language switchers, whose popovers read the brand as the menu
 * does; the theme; the icon that shows the navigation again, while it is hidden; and the close. The filter is the card's
 * first control, so it is what the overlay host focuses on opening: a user who summoned the whole tree has come to find
 * something in it.
 *
 * Filter and tree share a card on purpose. Given a panel each they would sit either side of
 * a gap and a second card edge, which reads as two unrelated screens rather than as one
 * control over one list. Further panels are for content that is genuinely separate, when
 * there is some.
 */
@Component({
  selector: 'ui-layout-mega-menu-body',
  imports: [
    NavComponent,
    BtnComponent,
    SearchBoxComponent,
    LayoutMenuEntryComponent,
    LayoutMenuGroupComponent,
    LayoutSwitchersComponent,
    LayoutThemeButtonComponent,
  ],
  template: `
    <!-- The nav: the filter and the switchers in the middle, the other controls fixed at the end, all 36 pixels high. -->
    <ui-nav class="shrink-0 border-b border-line bg-surface">
      <ui-search-box
        class="w-40"
        [text]="megaMenu.searchText()"
        [placeholder]="texts.of('SearchMenu')"
        [label]="texts.of('SearchMenu')"
        [debounce]="0"
        (searched)="megaMenu.search($event)"
      />
      <ui-layout-switchers size="medium" />

      <ng-container ngProjectAs="[end]">
        <ui-layout-theme-button size="medium" />
        @if (megaMenu.canRestoreNav()) {
          <ui-btn
            size="medium"
            variant="ghost"
            icon="panelRightClose"
            [title]="texts.of('NavigationShow')"
            (click)="megaMenu.restoreNav()"
          />
        }
        <ui-btn size="medium" variant="ghost" color="slate" icon="close" [title]="texts.of('Close')" (click)="megaMenu.close()" />
      </ng-container>
    </ui-nav>

    <div class="flex min-h-0 flex-1 flex-wrap content-start gap-(--ui-panel-gap) overflow-y-auto p-(--ui-panel-gap)">
      @for (module of megaMenu.modules(); track module.handle ?? $index) {
        @let modulePath = megaMenu.modulePathOf(module);

        <!--
          Blocks share the row and wrap to the next. They stop growing well before the overlay is wide: a column of
          short names set across half a screen is harder to scan than the same column at a readable width. Ringed with
          the tone between the block and the panel under it, so the block reads as raised from that panel.
        -->
        <section class="flex flex-1 basis-56 max-w-136 flex-col overflow-hidden rounded-lg border border-surface-2 bg-surface-3">
          <button
            type="button"
            class="flex cursor-pointer items-center bg-surface-3 px-2 py-1.5 text-left font-semibold hover:bg-active hover:text-on-active"
            [attr.title]="module.name"
            (click)="megaMenu.openModule(module)"
          >
            {{ module.name }}
          </button>

          @for (item of module.items; track item.handle ?? $index) {
            @if (isGroup(item)) {
              <ui-layout-menu-group
                [group]="item"
                [modulePath]="modulePath"
                [open]="megaMenu.isGroupOpen(module, item)"
                (toggled)="megaMenu.toggleGroup(module, item)"
                (opened)="megaMenu.close()"
              />
            } @else if (!item.isSeparator) {
              <ui-layout-menu-entry [leaf]="item" [modulePath]="modulePath" (opened)="megaMenu.close()" />
            }
          }
        </section>
      }
    </div>
  `,
  host: {
    // `text-base` is stated rather than inherited: the tree is set at the navigation's size wherever the overlay is
    // rendered.
    class: 'flex h-full flex-col bg-surface-4 text-base text-muted',
  },
})
export class LayoutMegaMenuBodyComponent {
  protected readonly texts = inject(TextsService);
  readonly megaMenu = inject(LayoutMegaMenuService);

  protected readonly isGroup = isMenuGroup;
}
