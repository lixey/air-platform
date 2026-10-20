import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { Router } from '@angular/router';
import { AppService } from '../app/app.service';
import { groupHoldsPath, isMenuGroup, modulePathOf, redirectFor } from '../app/menu-tree';
import { BtnComponent } from '../btn/btn.component';
import type { MenuItemDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { NavComponent } from '../nav/nav.component';
import { PANELS } from '../panels/panels-tokens';
import { LayoutMenuEntryComponent } from './layout-menu-entry.component';
import { LayoutMenuGroupComponent } from './layout-menu-group.component';
import { LayoutMenuGroups } from './layout-menu-groups';
import { LayoutSignOutComponent } from './layout-sign-out.component';
import { LayoutSwitcherComponent } from './layout-switcher.component';
import { LayoutThemeButtonComponent } from './layout-theme-button.component';

/**
 * The navigation tree: modules, the groups inside them, and the entries those hold.
 *
 * Three levels, and only the first and the last are addresses. A group is a node with
 * entries and no path of its own — it exists to fold rarely used entries out of sight, and
 * contributes nothing to a URL, so an entry keeps the same address whether it sits in a
 * group or beside one.
 *
 * Four grounds, one per layer: the tree sits on the fourth step of the surface ladder, a
 * module's band on the third, a group's band on the second, and every entry on the first. A
 * hairline in the lightest tone keeps a run of closed modules reading as a list.
 *
 * What marks a group's entries is a hairline, not a bar: the space they are indented by
 * carries their own ground, so a single line against a continuous surface does the work. Drawn
 * against the ground behind the tree instead, that same line reads as a thick stripe however
 * thin the border is.
 *
 * Hovering takes a weakened form of the colour a selected row carries, so it previews what
 * choosing would give rather than inventing a colour of its own.
 *
 * Every row is set at one size, whatever its level. A module and a group each carry a band
 * and a chevron and are told apart by weight; what a group holds is indented one step, with
 * a rule beside it. Hierarchy is carried by ground and position rather than by shrinking the
 * text, because every one of these rows is something the user has to read to choose it.
 *
 * A row is its label and nothing else. The mark that says an entry is pinned and the control
 * that pins it share one slot at the right edge, drawn over the end of the label rather than
 * beside it — so a row at rest spends none of its width on them, and the label keeps the
 * whole row. There is no leading icon: every entry had the same one, and it cost a quarter
 * of the width at the narrowest the navigation goes.
 *
 * The label is never hidden. However narrow the panel, it wraps to two lines and then
 * ellipsizes: an entry the user cannot read is an entry they cannot choose.
 */
@Component({
  selector: 'ui-layout-menu',
  imports: [
    UiIconDirective,
    NavComponent,
    BtnComponent,
    LayoutMenuEntryComponent,
    LayoutMenuGroupComponent,
    LayoutSignOutComponent,
    LayoutSwitcherComponent,
    LayoutThemeButtonComponent,
  ],
  template: `
    <nav class="flex min-h-0 flex-1 flex-col overflow-y-auto overflow-x-hidden bg-surface-4 select-none">
      @for (module of app.menu(); track module.handle ?? $index) {
        @let modulePath = pathOf(module);
        @let moduleOpen = isModuleOpen(module);

        <button
          type="button"
          class="flex cursor-pointer items-center gap-2 border-b border-surface bg-surface-3 py-2 pl-2 text-left font-semibold hover:bg-active hover:text-on-active"
          [attr.title]="module.name"
          [attr.aria-expanded]="moduleOpen"
          (click)="openModule(module)"
        >
          <span class="min-w-0 flex-1 leading-tight line-clamp-2 break-words">{{ module.name }}</span>
          <i class="flex w-7 shrink-0 items-center justify-center opacity-60" [icon]="moduleOpen ? 'chevron-bottom' : 'chevron-right'"></i>
        </button>

        @if (moduleOpen) {
          @for (item of module.items; track item.handle ?? $index) {
            @if (item.isSeparator) {
              <hr class="my-1 border-t border-line" />
            } @else if (isGroup(item)) {
              <!-- A group carries the band and the chevron a module carries; only the weight tells them apart. -->
              <ui-layout-menu-group
                [group]="item"
                [modulePath]="modulePath"
                [open]="groups.isOpen(module, item)"
                (toggled)="groups.toggle(module, item)"
              />
            } @else {
              <ui-layout-menu-entry [leaf]="item" [modulePath]="modulePath" />
            }
          }
        }
      }
    </nav>

    <!--
      The footer: the tenant and language switchers on a row of their own, each an equal share of it, then a nav with
      the theme control at its start and sign-out and the way to hide the navigation at its end. Every control is the
      library's button, small and ghost, because that is what a discrete control inside a card is. The switchers' row is
      as wide as the navigation and adds nothing to its width: a long name truncates rather than widening the panel.
    -->
    <div class="shrink-0 border-t border-line">
      @if (app.tenant(); as tenant) {
        <div class="flex w-0 min-w-full gap-(--ui-panel-gap) px-(--ui-panel-gap) pt-(--ui-panel-gap)">
          <ui-layout-switcher fluid [model]="tenant" />
        </div>
      }

      <ui-nav>
        <ng-container ngProjectAs="[start]">
          <ui-layout-theme-button />
          <ui-layout-switcher [model]="app.language()" />
        </ng-container>

        <ng-container ngProjectAs="[end]">
          <ui-layout-sign-out />

          @if (canHide()) {
            <ui-btn size="small" variant="ghost" icon="panelRightOpen" [title]="texts.of('NavigationHide')" (click)="hide()" />
          }
        </ng-container>
      </ui-nav>
    </div>
  `,
  host: {
    class: 'flex flex-col overflow-hidden grow min-h-0 min-w-0 w-full h-full bg-surface-4 text-muted',
  },
})
export class LayoutMenuComponent {
  protected readonly texts = inject(TextsService);
  readonly app = inject(AppService);
  private readonly router = inject(Router);

  private readonly panels = inject(PANELS, { optional: true });

  /** Where this panel sits in the layout, so the control below can close the right one. */
  readonly path = input<string>('');

  /** The module whose entries are showing. Follows the route until the user says otherwise. */
  readonly activeModule = linkedSignal<MenuItemDto>(() => this.app.currentModule());

  /**
   * The groups the user has opened or closed by hand. A group nobody has touched follows the
   * route instead, so arriving at an entry shows the branch it lives in.
   */
  protected readonly groups = new LayoutMenuGroups((module, group) => groupHoldsPath(modulePathOf(module), group, this.app.currentPath()));

  protected readonly canHide = computed(() => !!this.path() && !!this.panels);

  /** Whether the metadata carries a switcher: without one the footer draws no empty row for them. */
  protected readonly hasSwitchers = computed(() => !!this.app.tenant() || !!this.app.language());

  protected readonly isGroup = isMenuGroup;

  protected readonly pathOf = modulePathOf;

  protected isModuleOpen(module: MenuItemDto): boolean {
    return (this.activeModule().handle ?? '').toLowerCase() === (module.handle ?? '').toLowerCase();
  }

  /** A module header is a destination as well as a disclosure: it opens the accordion and the module's first leaf. */
  protected openModule(module: MenuItemDto): void {
    const target = redirectFor(module);
    // A header that leads nowhere has nothing left to open once its module is open
    const isAtTarget = !target || target.slice(1) === this.app.currentPath();
    if (this.isModuleOpen(module) && isAtTarget) {
      return;
    }

    this.activeModule.set(module);
    if (target) {
      void this.router.navigateByUrl(target);
    }
  }

  protected hide(): void {
    this.panels?.close(this.path());
  }
}
