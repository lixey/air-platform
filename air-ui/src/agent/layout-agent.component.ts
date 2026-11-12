import { Component, computed, DestroyRef, effect, ElementRef, inject, Injector, input, signal, viewChild } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { NavComponent } from '../nav/nav.component';
import { OverlayRef } from '../overlay/overlay-ref';
import { parseSize } from '../panels/panel-size';
import { PanelsState } from '../panels/panels-state';
import { PANELS } from '../panels/panels-tokens';
import type { SelectOption } from '../select/select-option';
import { SelectService } from '../select/select.service';
import { AgentComposerComponent } from './agent-composer.component';
import { AgentMessageItemComponent } from './agent-message-item.component';
import { type AgentChatMessage, AgentService } from './agent.service';

@Component({
  selector: 'ui-layout-agent',
  imports: [BtnComponent, NavComponent, UiIconDirective, AgentMessageItemComponent, AgentComposerComponent],
  providers: [SelectService],
  template: `
    <ui-nav class="shrink-0 border-b border-line bg-surface-2">
      <ng-container ngProjectAs="[start]">
        <div class="flex items-center gap-1.5 pl-2 font-semibold text-ink">
          <i class="flex items-center justify-center text-mark" [icon]="'bot'"></i>
          <span>{{ texts.of('AgentTitle') }}</span>
        </div>
      </ng-container>

      @if (agent.models().length > 0) {
        <ui-btn variant="ghost" [title]="modelTitle()" (click)="openModelSelect($event)">
          <span class="sr-only">{{ texts.of('AgentModel') + ': ' }}</span>
          <span class="min-w-0 max-w-28 truncate sm:max-w-40">{{ currentModelLabel() }}</span>
          <i [icon]="'chevron-bottom'" class="flex shrink-0 items-center justify-center opacity-60" aria-hidden="true"></i>
        </ui-btn>
      }

      <ng-container ngProjectAs="[end]">
        <ui-btn
          variant="ghost"
          icon="refresh"
          [title]="texts.of('AgentNewChat')"
          [disabled]="agent.isStreaming()"
          (click)="agent.reset()"
        />
        @if (canExpandPanel()) {
          <ui-btn
            variant="ghost"
            [icon]="isExpandedPanel() ? 'fullscreen-exit' : 'fullscreen'"
            [title]="texts.of(isExpandedPanel() ? 'ActualSize' : 'FitToScreen')"
            (click)="toggleExpandPanel()"
          />
        }
        @if (canClose()) {
          <ui-btn variant="ghost" icon="close" [title]="texts.of('Close')" (click)="closePanel()" />
        }
      </ng-container>
    </ui-nav>

    <div
      #scrollContainer
      class="relative flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto bg-field p-3"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      @if (isDragging()) {
        <div
          class="pointer-events-none absolute inset-2 z-20 flex flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-mark bg-surface/90 text-sm font-medium text-ink"
        >
          <i class="flex items-center justify-center text-mark" [icon]="'photo'"></i>
          <span>{{ texts.of('AgentDropHint') }}</span>
        </div>
      }

      @if (agent.messages().length === 0) {
        <div class="my-auto flex flex-col items-center justify-center gap-2.5 px-3 py-6 text-center text-muted">
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-surface-3 text-mark">
            <i class="flex items-center justify-center" [icon]="'bot'"></i>
          </div>
          <div class="font-semibold text-ink">{{ texts.of('AgentEmptyTitle') }}</div>
        </div>
      }

      @for (msg of agent.messages(); track msg.id) {
        <ui-agent-message-item
          [msg]="msg"
          [copied]="copiedId() === msg.id"
          (copy)="copyMessage($event)"
          (returnToInput)="returnToInput($event)"
        />
      }
    </div>

    <ui-agent-composer #composer />
  `,
  host: {
    class: 'flex flex-col overflow-hidden grow min-h-0 min-w-0 w-full h-full bg-surface text-ink',
  },
})
export class LayoutAgentComponent {
  readonly agent = inject(AgentService);
  protected readonly texts = inject(TextsService);
  private readonly panels = inject(PANELS, { optional: true });
  private readonly panelsState = inject(PanelsState, { optional: true });
  private readonly overlayRef = inject(OverlayRef, { optional: true });
  private readonly select = inject(SelectService);
  private readonly injector = inject(Injector);

  readonly path = input<string>('');
  protected readonly isDragging = signal(false);
  protected readonly copiedId = signal<string | null>(null);
  private copyTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly canExpandPanel = computed(() => !this.overlayRef && !!this.path() && !!this.panelsState);

  protected readonly isExpandedPanel = computed(() => {
    const p = this.path();
    const stored = p ? this.panelsState?.prefs()[p]?.size : undefined;
    if (!stored) {
      return false;
    }
    const parsed = parseSize(stored);
    return parsed.unit === 'px' && parsed.value >= 700;
  });

  private readonly scrollContainer = viewChild<ElementRef<HTMLElement>>('scrollContainer');
  private readonly composer = viewChild<AgentComposerComponent>('composer');

  protected readonly currentModelLabel = computed(() => {
    const id = this.agent.model();
    const prov = this.agent.activeProvider();
    const list = this.agent.models();
    return (list.find(m => m.id === id && (!prov || m.provider === prov)) ?? list.find(m => m.id === id))?.name ?? id;
  });

  protected readonly modelTitle = computed(() => `${this.texts.of('AgentModel')}: ${this.currentModelLabel()}`);

  constructor() {
    void this.agent.ensureStatusLoaded(true);

    const subscription = this.select.committed.subscribe(options => {
      const picked = options[0];
      if (!picked || picked.id === null) {
        return;
      }
      const model = this.agent.models()[picked.id];
      if (model) {
        this.agent.userPickedModel.set(!model.isDefault);
        this.agent.model.set(model.id);
        if (model.provider) {
          this.agent.activeProvider.set(model.provider);
        }
      }
    });

    inject(DestroyRef).onDestroy(() => {
      subscription.unsubscribe();
      if (this.copyTimer !== null) {
        clearTimeout(this.copyTimer);
        this.copyTimer = null;
      }
    });

    let lastMessageCount = 0;
    effect(() => {
      const msgs = this.agent.messages();
      const countChanged = msgs.length !== lastMessageCount;
      lastMessageCount = msgs.length;
      const el = this.scrollContainer()?.nativeElement;
      const wasNearBottom = !el || el.scrollHeight - el.scrollTop - el.clientHeight <= 80;
      if (countChanged || wasNearBottom) {
        queueMicrotask(() => {
          const container = this.scrollContainer()?.nativeElement;
          if (container) {
            container.scrollTop = container.scrollHeight;
          }
        });
      }
    });
  }

  protected openModelSelect(event: Event): void {
    const list = this.agent.models();
    if (list.length === 0) {
      return;
    }
    const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const options: SelectOption[] = list.map((m, idx) => ({
      id: idx,
      label: m.isForPhotos ? `${m.name} · 📷` : m.name,
    }));
    const prov = this.agent.activeProvider();
    const matchedIdx = list.findIndex(m => m.id === this.agent.model() && (!prov || m.provider === prov));
    const currentIdx = Math.max(0, matchedIdx >= 0 ? matchedIdx : list.findIndex(m => m.id === this.agent.model()));
    this.select.open({ model: { label: this.texts.of('AgentModel'), options }, picked: [options[currentIdx]] }, anchor, this.injector);
  }

  protected canClose(): boolean {
    return !!this.overlayRef || (!!this.path() && !!this.panels);
  }

  protected toggleExpandPanel(): void {
    const p = this.path();
    if (!p || !this.panelsState) {
      return;
    }
    this.panelsState.setSize(p, this.isExpandedPanel() ? '420px' : '920px');
  }

  protected closePanel(): void {
    if (this.overlayRef) {
      this.overlayRef.close();
      return;
    }
    this.panels?.close(this.path());
  }

  protected onDragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      this.isDragging.set(true);
    }
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    if (
      event.currentTarget instanceof HTMLElement &&
      event.relatedTarget instanceof Node &&
      event.currentTarget.contains(event.relatedTarget)
    ) {
      return;
    }
    this.isDragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(false);
    if (event.dataTransfer?.files && event.dataTransfer.files.length > 0) {
      void this.agent.uploadFiles(event.dataTransfer.files);
    }
  }

  protected copyMessage(msg: AgentChatMessage): void {
    if (!msg.text || typeof navigator === 'undefined' || !navigator.clipboard) {
      return;
    }
    void navigator.clipboard.writeText(msg.text).then(() => {
      this.copiedId.set(msg.id);
      if (this.copyTimer) {
        clearTimeout(this.copyTimer);
      }
      this.copyTimer = setTimeout(() => {
        if (this.copiedId() === msg.id) {
          this.copiedId.set(null);
        }
      }, 1500);
    });
  }

  protected returnToInput(text: string): void {
    this.composer()?.setDraftAndFocus(text);
  }
}
