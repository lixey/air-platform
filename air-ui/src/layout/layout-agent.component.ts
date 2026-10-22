import { DecimalPipe } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { BadgeComponent } from '../badge/badge.component';
import { BtnComponent } from '../btn/btn.component';
import type { UiColor } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { NavComponent } from '../nav/nav.component';
import { OverlayRef } from '../overlay/overlay-ref';
import { PANELS } from '../panels/panels-tokens';
import type { SelectOption } from '../select/select-option';
import { SelectService } from '../select/select.service';
import { fieldClasses } from '../theme/css-classes';
import { AgentService, type AgentProposal } from './agent.service';

const MAX_PROMPT_HEIGHT = 160;

function fitPromptToText(area: HTMLTextAreaElement): void {
  if (!area.value) {
    area.style.height = '2.25rem';
    area.style.overflowY = 'hidden';
    return;
  }
  area.style.height = 'auto';
  const full = area.scrollHeight + area.offsetHeight - area.clientHeight;
  const clamped = Math.min(MAX_PROMPT_HEIGHT, Math.max(36, full));
  area.style.height = `${clamped}px`;
  area.style.overflowY = full > MAX_PROMPT_HEIGHT ? 'auto' : 'hidden';
}

@Component({
  selector: 'ui-layout-agent',
  imports: [DecimalPipe, BtnComponent, BadgeComponent, NavComponent, UiIconDirective],
  providers: [SelectService],
  template: `
    <ui-nav class="shrink-0 border-b border-line bg-surface-2">
      <ng-container ngProjectAs="[start]">
        <div class="flex items-center gap-1.5 pl-2 font-semibold text-ink">
          <i class="flex items-center justify-center text-mark" [icon]="'bot'"></i>
          <span>{{ texts.of('AgentTitle') }}</span>
        </div>
        @if (agent.models().length > 0) {
          <ui-btn variant="ghost" [title]="modelTitle()" (click)="openModelSelect($event)">
            <span class="sr-only">{{ texts.of('AgentModel') + ': ' }}</span>
            <span class="min-w-0 max-w-40 truncate">{{ currentModelLabel() }}</span>
            <i icon="chevron-bottom" class="shrink-0 opacity-60" aria-hidden="true"></i>
          </ui-btn>
        }
      </ng-container>

      <ng-container ngProjectAs="[end]">
        <ui-btn variant="ghost" icon="reload" [title]="texts.of('AgentNewChat')" [disabled]="agent.isStreaming()" (click)="agent.reset()" />
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
        @if (msg.role === 'user') {
          <div class="ml-6 flex flex-col items-end gap-1.5">
            @if (msg.attachments.length > 0) {
              <div class="flex flex-wrap justify-end gap-1.5">
                @for (att of msg.attachments; track att.id) {
                  <a
                    [href]="att.previewUrl"
                    target="_blank"
                    rel="noopener"
                    class="block overflow-hidden rounded-md border border-line bg-surface-2"
                    [title]="att.fileName"
                  >
                    <img [src]="att.previewUrl" [alt]="att.fileName" class="h-24 max-w-36 object-cover" />
                  </a>
                }
              </div>
            }
            @if (msg.text) {
              <div class="rounded-lg bg-active px-3 py-2 text-sm whitespace-pre-wrap text-on-active break-words">
                {{ msg.text }}
              </div>
            }
          </div>
        } @else {
          <div class="mr-3 flex flex-col items-start gap-2">
            @if (msg.text) {
              <div class="rounded-lg border border-line bg-surface-2 px-3 py-2 text-sm whitespace-pre-wrap text-ink break-words">
                {{ msg.text }}
              </div>
            }

            @if (msg.statusText) {
              <div class="flex items-center gap-2 px-1 text-xs text-muted">
                <i class="h-3.5 w-3.5 text-mark" [icon]="'spinner'"></i>
                <span>{{ msg.statusText }}</span>
              </div>
            }

            @for (proposal of msg.proposals; track proposal.id) {
              <div class="w-full overflow-hidden rounded-lg border border-line bg-surface-2 shadow-xs">
                <div class="flex items-center justify-between gap-2 border-b border-line bg-surface-3 px-3 py-2">
                  <div class="min-w-0 flex-1">
                    <div class="truncate text-sm font-semibold text-ink">{{ proposal.title }}</div>
                    @if (proposal.subtitle) {
                      <div class="truncate text-xs text-muted">{{ proposal.subtitle }}</div>
                    }
                  </div>
                  @if (proposal.badge) {
                    <ui-badge [label]="proposal.badge" [color]="asColor(proposal.badgeColor)" />
                  }
                </div>

                <div class="divide-y divide-line/60 px-3 py-1.5 text-xs">
                  @for (field of proposal.fields; track field.label) {
                    <div class="flex items-center justify-between gap-2 py-1">
                      <span class="text-muted">{{ field.label }}</span>
                      <span class="flex items-center gap-1.5 font-medium text-ink text-right">
                        <span>{{ field.value }}</span>
                        @if (field.badge) {
                          <ui-badge [label]="field.badge" [color]="asColor(field.color)" />
                        }
                      </span>
                    </div>
                  }
                </div>

                @if (proposal.lines.length > 0) {
                  <div class="border-t border-line bg-surface px-3 py-2">
                    <div class="mb-1.5 text-[11px] font-semibold tracking-wide uppercase text-muted">
                      {{ proposal.linesLabel || texts.of('AgentLinesTitle', proposal.lines.length) }}
                    </div>
                    <div class="max-h-44 space-y-1 overflow-y-auto pr-1 text-xs">
                      @for (line of proposal.lines; track $index) {
                        <div class="flex items-start justify-between gap-2 border-b border-line/40 pb-1 last:border-b-0 last:pb-0">
                          <div class="min-w-0 flex-1">
                            <div class="truncate text-ink" [title]="line.name">{{ line.name }}</div>
                            <div class="text-[11px] text-muted">
                              {{ line.qty | number: '1.0-3' }} × {{ line.price | number: '1.2-2' }}
                              @if (line.category) {
                                <span>· {{ line.category }}</span>
                              }
                            </div>
                          </div>
                          <div class="shrink-0 font-medium tabular-nums text-ink">
                            {{ line.amount | number: '1.2-2' }}
                          </div>
                        </div>
                      }
                    </div>
                  </div>
                }

                @if (proposal.attachmentIds.length > 0) {
                  <div class="flex items-center gap-1.5 border-t border-line bg-surface-3/50 px-3 py-1.5 text-xs text-muted">
                    <i class="h-3.5 w-3.5" [icon]="'photo'"></i>
                    <span>{{ texts.of('AgentPhotoWillAttach') }}</span>
                  </div>
                }

                <div class="flex items-center justify-between gap-2 border-t border-line bg-surface-3 px-3 py-2">
                  @if (proposal.status === 'committed') {
                    <div class="flex items-center gap-1.5 text-xs font-semibold text-green-600 dark:text-green-400">
                      <i class="h-4 w-4" [icon]="'checkmark'"></i>
                      <span>{{ texts.of('AgentSaved', proposal.committedLabel ?? '') }}</span>
                    </div>
                    <ui-btn
                      size="small"
                      variant="ghost"
                      color="blue"
                      icon="external"
                      [label]="texts.of('Open')"
                      (click)="openCommitted(proposal)"
                    />
                  } @else {
                    <span class="text-xs text-muted">{{ texts.of('AgentCheckBeforeSave') }}</span>
                    <ui-btn
                      size="small"
                      variant="soft"
                      color="green"
                      icon="checkmark"
                      [label]="texts.of('AgentConfirm')"
                      [loading]="proposal.status === 'confirming'"
                      (click)="agent.confirmProposal(msg.id, proposal.id)"
                    />
                  }
                </div>
              </div>
            }
          </div>
        }
      }
    </div>

    <div class="shrink-0 border-t border-line bg-surface p-2">
      @if (agent.pendingAttachments().length > 0) {
        <div class="mb-2 flex flex-wrap gap-1.5">
          @for (att of agent.pendingAttachments(); track att.id) {
            <div class="relative overflow-hidden rounded-md border border-line bg-surface">
              <img [src]="att.previewUrl" [alt]="att.fileName" class="h-14 w-14 object-cover" />
              <button
                type="button"
                class="absolute top-0.5 right-0.5 flex h-4 w-4 cursor-pointer items-center justify-center rounded-full bg-surface/90 text-ink hover:bg-red-500 hover:text-white"
                [title]="texts.of('Delete')"
                (click)="agent.removePendingAttachment(att.id)"
              >
                <i class="flex items-center justify-center" [icon]="'close'"></i>
              </button>
            </div>
          }
        </div>
      }

      <input #fileInput type="file" accept="image/*" multiple class="hidden" (change)="onFilesPicked($event)" />

      <div class="flex items-start gap-1.5">
        <ui-btn
          size="medium"
          variant="soft"
          color="blue"
          icon="photo"
          [title]="texts.of('AgentAttachTitle')"
          [loading]="agent.isUploading()"
          (click)="fileInput.click()"
        />

        <textarea
          #promptInput
          rows="1"
          [class]="fieldClasses(false) + ' flex-1 min-h-9 max-h-40 py-1.25 leading-6 resize-none overflow-hidden'"
          [placeholder]="texts.of('AgentInputPlaceholder')"
          [value]="draft()"
          (input)="draft.set(promptInput.value)"
          (keydown)="onKeydown($event)"
          (paste)="onPaste($event)"
        ></textarea>

        @if (agent.isStreaming()) {
          <ui-btn size="medium" variant="soft" color="red" icon="close" [title]="texts.of('AgentStopTitle')" (click)="agent.stop()" />
        } @else {
          <ui-btn
            size="medium"
            variant="soft"
            color="blue"
            icon="arrow-right"
            [title]="texts.of('AgentSendTitle')"
            [disabled]="!canSend()"
            (click)="submit()"
          />
        }
      </div>
    </div>
  `,
  host: {
    class: 'flex flex-col overflow-hidden grow min-h-0 min-w-0 w-full h-full bg-surface text-ink',
  },
})
export class LayoutAgentComponent {
  readonly agent = inject(AgentService);
  protected readonly texts = inject(TextsService);
  private readonly panels = inject(PANELS, { optional: true });
  private readonly overlayRef = inject(OverlayRef, { optional: true });
  private readonly select = inject(SelectService);
  private readonly injector = inject(Injector);

  readonly path = input<string>('');
  protected readonly draft = signal('');
  protected readonly isDragging = signal(false);
  protected readonly fieldClasses = fieldClasses;

  private readonly scrollContainer = viewChild<ElementRef<HTMLElement>>('scrollContainer');
  private readonly promptInput = viewChild<ElementRef<HTMLTextAreaElement>>('promptInput');
  private readonly areaWidth = signal(0);

  protected readonly currentModelLabel = computed(() => {
    const id = this.agent.model();
    const list = this.agent.models();
    return list.find(m => m.id === id)?.name ?? id;
  });

  protected readonly modelTitle = computed(() => `${this.texts.of('AgentModel')}: ${this.currentModelLabel()}`);

  private readonly watchPromptWidth = afterRenderEffect(onCleanup => {
    const area = this.promptInput()?.nativeElement;
    if (!area || typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver(([entry]) => this.areaWidth.set(entry?.contentRect.width ?? 0));
    observer.observe(area);
    onCleanup(() => observer.disconnect());
  });

  private readonly fitPromptArea = afterRenderEffect({
    write: () => {
      this.draft();
      this.areaWidth();
      const area = this.promptInput()?.nativeElement;
      if (area) {
        fitPromptToText(area);
      }
    },
  });

  constructor() {
    void this.agent.ensureStatusLoaded();

    const subscription = this.select.committed.subscribe(options => {
      const picked = options[0];
      if (!picked) {
        return;
      }
      const model = this.agent.models()[picked.id];
      if (model) {
        this.agent.model.set(model.id);
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());

    effect(() => {
      this.agent.messages();
      queueMicrotask(() => {
        const el = this.scrollContainer()?.nativeElement;
        if (el) {
          el.scrollTop = el.scrollHeight;
        }
      });
    });
  }

  protected openModelSelect(event: Event): void {
    const list = this.agent.models();
    if (list.length === 0) {
      return;
    }
    const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const options: SelectOption[] = list.map((m, idx) => ({ id: idx, label: m.name }));
    const currentIdx = Math.max(
      0,
      list.findIndex(m => m.id === this.agent.model()),
    );
    this.select.open({ model: { label: this.texts.of('AgentModel'), options }, picked: [options[currentIdx]] }, anchor, this.injector);
  }

  protected canClose(): boolean {
    return !!this.overlayRef || (!!this.path() && !!this.panels);
  }

  protected closePanel(): void {
    if (this.overlayRef) {
      this.overlayRef.close();
      return;
    }
    this.panels?.close(this.path());
  }

  protected canSend(): boolean {
    return (this.draft().trim().length > 0 || this.agent.pendingAttachments().length > 0) && !this.agent.isUploading();
  }

  protected async submit(): Promise<void> {
    if (!this.canSend()) {
      return;
    }
    const text = this.draft();
    this.draft.set('');
    await this.agent.send(text);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.submit();
    }
  }

  protected onFilesPicked(event: Event): void {
    const inputEl = event.target as HTMLInputElement;
    if (inputEl.files && inputEl.files.length > 0) {
      void this.agent.uploadFiles(inputEl.files);
      inputEl.value = '';
    }
  }

  protected onPaste(event: ClipboardEvent): void {
    const items = event.clipboardData?.items;
    if (!items) {
      return;
    }

    const imageFiles: File[] = [];
    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      if (item.type.startsWith('image/')) {
        const file = item.getAsFile();
        if (file) {
          imageFiles.push(file);
        }
      }
    }

    if (imageFiles.length > 0) {
      event.preventDefault();
      void this.agent.uploadFiles(imageFiles);
    }
  }

  protected onDragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      this.isDragging.set(true);
    }
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(false);
    if (event.dataTransfer?.files && event.dataTransfer.files.length > 0) {
      void this.agent.uploadFiles(event.dataTransfer.files);
    }
  }

  protected openCommitted(proposal: AgentProposal): void {
    this.agent.openRecord(proposal);
  }

  protected asColor(color: string | null | undefined): UiColor {
    return (color as UiColor) || 'slate';
  }
}
