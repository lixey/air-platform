import { afterNextRender, Component, computed, DestroyRef, effect, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { AppService } from '../app/app.service';
import { LocaleService } from '../app/locale.service';
import type { OverlayModelDto, PanelLayoutDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { OverlayHostComponent } from '../overlay/overlay-host.component';
import { type OverlayRef } from '../overlay/overlay-ref';
import { OverlayService } from '../overlay/overlay.service';
import { PanelsComponent } from '../panels/panels.component';
import { singlePanelLayout } from '../panels/single-panel-layout';
import { AGENT_PANEL, MENU_PANEL, TABS_PANEL, WORK_PANEL } from './layout-panel-keys';
import { LayoutPinsService } from './layout-pins.service';
import { isTabStripVisible } from './layout-tabs.component';

const AGENT_DIALOG: OverlayModelDto = {
  key: 'layout.agent',
  presentation: 'dialog',
  closesOnEscape: true,
  closesOnOutside: true,
  width: '100%',
  height: '100%',
  layout: singlePanelLayout(AGENT_PANEL, { kind: 'LayoutAgent' }),
};

/**
 * The frame's own panels: the navigation beside the work area, and in the work area the strip above whatever the
 * route resolved.
 */
export const SHELL_LAYOUT: PanelLayoutDto = {
  key: 'app.shell',
  root: {
    key: 'root',
    direction: 'row',
    children: [
      {
        // Sized by its entries, no wider than 320px and with no minimum of its own: short
        // entries leave the work the most room, and an opened group widens the column so every
        // entry is read whole. Hidden under 1280px of frame, which is a window of about 1288 —
        // `xl` — and reached then through the strip's menu trigger.
        key: MENU_PANEL,
        model: { kind: 'LayoutMenu' },
        size: 'auto',
        minSize: '128px',
        maxSize: '320px',
        isResizable: true,
        hideUnder: 1280,
      },
      {
        key: AGENT_PANEL,
        model: { kind: 'LayoutAgent' },
        size: '420px',
        minSize: '320px',
        maxSize: '1200px',
        hideUnder: 1024,
        isResizable: true,
        isCollapsed: true,
      },
      {
        key: 'work',
        direction: 'column',
        size: '1fr',
        minSize: '320px',
        children: [
          {
            // Sized to what it holds, so it costs nothing while it holds nothing. Pinned,
            // so that below `lg` it is what stays above the content: the menu trigger and
            // the entries the user keeps at hand.
            key: 'tabs',
            model: { kind: 'LayoutTabs' },
            size: 'auto',
            isTransparent: true,
            isPinned: true,
          },
          {
            key: 'main',
            model: { kind: 'LayoutRouteScreen' },
            size: '1fr',
          },
        ],
      },
    ],
  },
};

/**
 * The application frame: navigation on the left, the work area beside it.
 *
 * The work area is a column of two panels — the strip of what the user keeps at hand, and
 * whatever the route resolved. The strip's panel is declared transparent, so the strip lies
 * on the page while the content below keeps its card, and the gap between them is the gap
 * the engine puts between any two panels.
 *
 * The strip's panel is closed outright while the strip has nothing to show. A panel that is
 * present but measures nothing still takes the gap beside it, and the routed content would
 * then sit four pixels below the navigation next to it — which is exactly the kind of
 * misalignment nobody can name but everybody sees.
 *
 * These are the only panels the shell owns. Everything deeper arrives with the screen the
 * route names, as its own layout — so navigating swaps the contents of `main` and leaves the
 * navigation and the strip exactly where the user put them.
 *
 * While a modal overlay is open the frame's panels are inert: neither the virtual cursor nor a
 * pointer reaches the page under a modal dialog.
 *
 * The frame also hosts the overlays, beside its panels rather than inside one: a panel is a
 * container, and a fixed box inside a container is positioned against it. It measures the
 * work area for them, so a dialog centres over the work and a drawer stands against the
 * navigation's wall wherever the user has dragged it.
 */
@Component({
  selector: 'ui-layout',
  imports: [PanelsComponent, OverlayHostComponent, UiIconDirective],
  template: `
    <ui-panels [layout]="layout" [attr.inert]="overlay.hasModal() ? '' : null" />
    @if (overlay.bounds(); as b) {
      <button
        type="button"
        class="fixed z-40 flex h-10 w-10 -translate-y-full cursor-pointer items-center justify-center rounded-tr-xl rounded-bl-xl border border-slate-400 bg-slate-200 text-mark transition-colors hover:bg-slate-300 dark:border-slate-600 dark:bg-slate-700 dark:hover:bg-slate-600"
        [class.bg-slate-300!]="isAgentOpen()"
        [class.dark:bg-slate-600!]="isAgentOpen()"
        [style.left.px]="b.left"
        [style.top.px]="b.top + b.height"
        [title]="texts.of('AgentTitle')"
        [attr.aria-label]="texts.of('AgentTitle')"
        (click)="toggleAgent()"
      >
        <i class="flex items-center justify-center text-mark" [icon]="'bot'"></i>
      </button>
    }
    <ui-overlay-host />
  `,
  host: {
    class: 'flex flex-col grow shrink overflow-hidden min-h-0 min-w-0 w-full h-full bg-page',
  },
})
export class LayoutComponent {
  readonly app = inject(AppService);
  protected readonly texts = inject(TextsService);

  private readonly pins = inject(LayoutPinsService);
  /** Constructed with the shell, so the document speaks the session's language after a reload too. */
  private readonly locale = inject(LocaleService);
  protected readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * The layout this frame renders. Reached through the view because the engine is what
   * provides it, and read only after the first render: it cannot answer before it has been
   * given a layout.
   */
  private readonly panels = viewChild(PanelsComponent);
  private readonly isEngineReady = signal(false);
  private readonly agentDialogRef = signal<OverlayRef | null>(null);

  protected readonly layout = SHELL_LAYOUT;
  protected readonly isAgentOpen = computed(() => {
    if (this.agentDialogRef()) {
      return true;
    }
    const panels = this.panels();
    return this.isEngineReady() && !!panels && !panels.isCollapsed(AGENT_PANEL);
  });

  protected toggleAgent(): void {
    const existingDialog = this.agentDialogRef();
    if (existingDialog) {
      existingDialog.close();
      return;
    }

    const panels = this.panels();
    if (!panels) {
      return;
    }

    if (panels.isNarrow()) {
      const ref = this.overlay.open(AGENT_DIALOG, this.host.nativeElement, this.injector);
      this.agentDialogRef.set(ref);
      ref.closed.subscribe(() => {
        if (this.agentDialogRef() === ref) {
          this.agentDialogRef.set(null);
        }
      });
      return;
    }

    panels.toggle(AGENT_PANEL);
  }

  constructor() {
    afterNextRender(() => {
      this.isEngineReady.set(true);
      this.watchWorkArea();
    });

    effect(() => {
      const panels = this.panels();

      if (!this.isEngineReady() || !panels) {
        return;
      }

      // The strip earns its panel when the user has pinned something, or when it is the only
      // way left to reach the menu.
      const hasSomethingToShow = isTabStripVisible(this.pins.pins(), panels.isCollapsed(MENU_PANEL));
      const isClosed = panels.isCollapsed(TABS_PANEL);

      // Asked only when the answer would change something: opening what is already open
      // rewrites the stored arrangement, and this effect reads what it writes.
      if (hasSomethingToShow && isClosed) {
        panels.open(TABS_PANEL);
      } else if (!hasSomethingToShow && !isClosed) {
        panels.close(TABS_PANEL);
      }

      if (!panels.isNarrow() && this.agentDialogRef()) {
        this.agentDialogRef()?.close();
        panels.open(AGENT_PANEL);
      }
    });
  }

  /**
   * Tells the overlays where the work area is, and keeps telling them: dragging the
   * navigation resizes the work area, so one observer on it sees both size and position.
   */
  private watchWorkArea(): void {
    const work = (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-panel="${WORK_PANEL}"]`);

    if (!work || typeof ResizeObserver === 'undefined') {
      return;
    }

    const observer = new ResizeObserver(() => {
      const { left, top, width, height } = work.getBoundingClientRect();
      this.overlay.setBounds({ left, top, width, height });
    });

    observer.observe(work);

    this.destroyRef.onDestroy(() => {
      observer.disconnect();
      this.overlay.setBounds(null);
    });
  }
}
