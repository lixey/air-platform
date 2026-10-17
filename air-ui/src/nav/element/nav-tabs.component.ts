import { Component, computed, inject, model } from '@angular/core';
import { BtnTablistDirective } from '../../btn/btn-tablist.directive';
import { BtnComponent } from '../../btn/btn.component';
import type { NavLabelModel, NavTabsModel } from '../../core/generated/contracts';
import { exclusiveContainerOf, PANEL_NODE } from '../../panels/panels-tokens';
import type { PanelView } from '../../panels/panels-view';
import { NavLabelComponent } from './nav-label.component';

/**
 * The tabs of the exclusive container around the nav: one button per page, pressed while
 * the page is open, in the look of the model. The container is found by walking up from the
 * panel the nav stands in, across nested layouts, so a grid's nav needs no path.
 *
 * The element is drawn wherever the server composed it — a strip a page draws above its
 * content, the nav of a pinned child — unless its model says `isPagedOnly`: then only while the
 * container is paged, which is how the form's nav takes the tabs over once the page draws no
 * strip. A pinned child that has become a page is not among the buttons: the caption or tab
 * that names its path is its tab. The single page of a container that is not paged is drawn as
 * a caption; a paged container always has a pinned child among its pages, so there the one
 * button left is still a tab. Outside an exclusive container nothing is drawn.
 *
 * While the container is paged the buttons are the tabs of one tablist, since one page is
 * always open; beside it a page may be open, or none, and they stay toggles — but for the page
 * the element itself stands in: a strip a page draws does not close that page, which would
 * hide the strip with it, and its pressed button stays pressed. The nav of a pinned child
 * stands in no page and closes any.
 */
@Component({
  selector: 'ui-nav-tabs',
  imports: [BtnComponent, BtnTablistDirective, NavLabelComponent],
  template: `
    @if (isDrawn()) {
      @let list = pages();
      @if (isPaged()) {
        <div uiTablist class="flex flex-none items-center gap-(--ui-panel-gap)">
          @for (page of list; track page.path) {
            <ui-btn role="tab" [model]="model()" [label]="page.name" [selected]="!page.isCollapsed" (click)="activate(page)" />
          }
        </div>
      } @else if (list.length === 1) {
        <ui-nav-label [model]="captionOf(list[0])" />
      } @else {
        @for (page of list; track page.path) {
          <ui-btn [model]="model()" [label]="page.name" [selected]="!page.isCollapsed" (click)="activate(page)" />
        }
      }
    }
  `,
  host: { class: 'contents' },
})
export class NavTabsComponent {
  private readonly node = inject(PANEL_NODE, { optional: true });

  readonly model = model<NavTabsModel | null>();

  /** The exclusive container around the nav; the chain of nodes never changes under a mounted element. */
  private readonly container = exclusiveContainerOf(this.node);

  /** While the container is paged one page is always open: the buttons are tabs. Beside it they show and hide pages. */
  protected readonly isPaged = computed(() => this.container?.view().isPaged === true);

  /** Inside an exclusive container, and — when the model says so — only while it is paged. */
  protected readonly isDrawn = computed(() => !!this.container && (!this.model()?.isPagedOnly || this.container.view().isPaged));

  /** The pages, in declared order, but for a pinned child that has become one: its caption is its tab. */
  protected readonly pages = computed<readonly PanelView[]>(() =>
    this.container ? this.container.view().children.filter(child => child.isPage && !child.node.isPinned) : [],
  );

  /** A page's button: the look every page's button takes, under the page's name. */
  protected captionOf(page: PanelView): NavLabelModel {
    return { ...this.model(), kind: 'NavLabel', key: page.path, label: page.name };
  }

  /** Opens a closed page; toggles an open one, unless the element stands in it: then the press does nothing. */
  protected activate(page: PanelView): void {
    const panels = this.container?.panels;

    if (!panels) {
      return;
    }

    // The engine decides whether the open page may close: a paged container opens the first instead.
    if (page.isCollapsed) {
      panels.open(page.path);
    } else if (!this.standsIn(page)) {
      panels.toggle(page.path);
    }
  }

  /** Whether this element stands in that page: the child of the container on the way up from the element's panel is the page. */
  private standsIn(page: PanelView): boolean {
    for (let member = this.node; member; member = member.parent) {
      if (member.parent === this.container) {
        return member.path() === page.path;
      }
    }

    return false;
  }
}
