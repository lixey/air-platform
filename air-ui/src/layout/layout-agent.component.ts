import { DecimalPipe } from '@angular/common';
import { Component, effect, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { BadgeComponent } from '../badge/badge.component';
import { BtnComponent } from '../btn/btn.component';
import type { UiColor } from '../core/generated/contracts';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { NavComponent } from '../nav/nav.component';
import { PANELS } from '../panels/panels-tokens';
import { AgentService, type AgentProposal } from './agent.service';

@Component({
  selector: 'ui-layout-agent',
  imports: [DecimalPipe, BtnComponent, BadgeComponent, NavComponent, UiIconDirective],
  template: `
    <ui-nav class="shrink-0 border-b border-line bg-surface-2">
      <ng-container ngProjectAs="[start]">
        <div class="flex items-center gap-1.5 pl-2 font-semibold text-ink">
          <i class="h-4 w-4 text-mark" [icon]="'star'"></i>
          <span>AI Асистент</span>
          <select
            #modelSelect
            class="cursor-pointer rounded border border-line bg-surface px-1.5 py-0.5 text-xs font-normal text-ink outline-none focus:border-mark"
            title="Модель AI"
            [value]="agent.model()"
            (change)="agent.model.set(modelSelect.value)"
          >
            @for (m of models; track m.id) {
              <option [value]="m.id">{{ m.label }}</option>
            }
          </select>
        </div>
      </ng-container>

      <ng-container ngProjectAs="[end]">
        <ui-btn
          size="small"
          variant="ghost"
          icon="reload"
          title="Новий діалог"
          [disabled]="agent.isStreaming()"
          (click)="agent.reset()"
        />
        @if (canClose()) {
          <ui-btn size="small" variant="ghost" icon="close" title="Закрити" (click)="closePanel()" />
        }
      </ng-container>
    </ui-nav>

    <div
      #scrollContainer
      class="relative flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-3"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      @if (isDragging()) {
        <div
          class="pointer-events-none absolute inset-2 z-20 flex flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-mark bg-surface/90 text-sm font-medium text-ink"
        >
          <i class="h-6 w-6 text-mark" [icon]="'photo'"></i>
          <span>Перетягніть фото чека сюди</span>
        </div>
      }

      @if (agent.messages().length === 0) {
        <div class="my-auto flex flex-col items-center justify-center gap-3 px-3 py-6 text-center text-muted">
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-surface-3 text-mark">
            <i class="h-5 w-5" [icon]="'star'"></i>
          </div>
          <div class="space-y-1">
            <div class="font-semibold text-ink">Фінансовий AI агент</div>
            <div class="text-xs leading-relaxed">
              Напишіть операцію своїми словами або закиньте фото чека (перетягніть чи натисніть <kbd class="rounded border border-line bg-surface-2 px-1">⌘V</kbd>).
            </div>
          </div>

          <div class="mt-2 flex flex-wrap justify-center gap-1.5">
            @for (sample of samples; track sample) {
              <button
                type="button"
                class="cursor-pointer rounded-md border border-line bg-surface-2 px-2.5 py-1 text-left text-xs text-ink transition-colors hover:bg-active hover:text-on-active"
                (click)="useSample(sample)"
              >
                {{ sample }}
              </button>
            }
          </div>
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
                      Позиції чека ({{ proposal.lines.length }})
                    </div>
                    <div class="max-h-44 space-y-1 overflow-y-auto pr-1 text-xs">
                      @for (line of proposal.lines; track $index) {
                        <div class="flex items-start justify-between gap-2 border-b border-line/40 pb-1 last:border-b-0 last:pb-0">
                          <div class="min-w-0 flex-1">
                            <div class="truncate text-ink" [title]="line.name">{{ line.name }}</div>
                            <div class="text-[11px] text-muted">
                              {{ line.qty | number: '1.0-3' }} × {{ line.price | number: '1.2-2' }}
                              @if (line.category) {
                                <span> · {{ line.category }}</span>
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
                    <span>Фото чека буде прикріплено до операції</span>
                  </div>
                }

                <div class="flex items-center justify-between gap-2 border-t border-line bg-surface-3 px-3 py-2">
                  @if (proposal.status === 'committed') {
                    <div class="flex items-center gap-1.5 text-xs font-semibold text-green-600 dark:text-green-400">
                      <i class="h-4 w-4" [icon]="'checkmark'"></i>
                      <span>Збережено: {{ proposal.committedLabel }}</span>
                    </div>
                    <ui-btn
                      size="small"
                      variant="ghost"
                      color="blue"
                      icon="external"
                      label="Відкрити"
                      (click)="openCommitted(proposal)"
                    />
                  } @else {
                    <span class="text-xs text-muted">Перевірте дані перед збереженням</span>
                    <ui-btn
                      size="small"
                      variant="soft"
                      color="green"
                      icon="checkmark"
                      label="Підтверджую"
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

    <div class="shrink-0 border-t border-line bg-surface-2 p-2">
      @if (agent.pendingAttachments().length > 0) {
        <div class="mb-2 flex flex-wrap gap-1.5">
          @for (att of agent.pendingAttachments(); track att.id) {
            <div class="relative overflow-hidden rounded-md border border-line bg-surface">
              <img [src]="att.previewUrl" [alt]="att.fileName" class="h-14 w-14 object-cover" />
              <button
                type="button"
                class="absolute top-0.5 right-0.5 flex h-4 w-4 cursor-pointer items-center justify-center rounded-full bg-surface/90 text-ink hover:bg-red-500 hover:text-white"
                title="Видалити"
                (click)="agent.removePendingAttachment(att.id)"
              >
                <i class="h-3 w-3" [icon]="'close'"></i>
              </button>
            </div>
          }
        </div>
      }

      <input
        #fileInput
        type="file"
        accept="image/*"
        multiple
        class="hidden"
        (change)="onFilesPicked($event)"
      />

      <div class="flex items-end gap-1.5">
        <ui-btn
          size="medium"
          variant="ghost"
          icon="photo"
          title="Прикріпити фото чека"
          [loading]="agent.isUploading()"
          (click)="fileInput.click()"
        />

        <textarea
          #promptInput
          rows="2"
          class="min-h-9 max-h-28 flex-1 resize-none rounded-md border border-control-line bg-control px-2.5 py-1.5 text-sm text-ink outline-none focus:border-mark"
          placeholder="Напишіть операцію або вставте фото чека…"
          [value]="draft()"
          (input)="draft.set(promptInput.value)"
          (keydown)="onKeydown($event)"
          (paste)="onPaste($event)"
        ></textarea>

        @if (agent.isStreaming()) {
          <ui-btn
            size="medium"
            variant="soft"
            color="red"
            icon="close"
            title="Зупинити"
            (click)="agent.stop()"
          />
        } @else {
          <ui-btn
            size="medium"
            variant="soft"
            color="blue"
            icon="arrow-right"
            title="Надіслати (Enter)"
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
  private readonly panels = inject(PANELS, { optional: true });

  readonly path = input<string>('');
  protected readonly draft = signal('');
  protected readonly isDragging = signal(false);

  private readonly scrollContainer = viewChild<ElementRef<HTMLElement>>('scrollContainer');

  protected readonly models = [
    { id: 'gemini-3.8-flash-low', label: '3.8 Flash (Fast)' },
    { id: 'gemini-3.8-flash-medium', label: '3.8 Flash (Medium)' },
    { id: 'gemini-3.8-flash-high', label: '3.8 Flash (High)' },
    { id: 'gemini-4-argon-medium', label: '4 Argon' },
    { id: 'claude-sonnet-5-5-low', label: 'Sonnet 5.5' },
  ];

  protected readonly samples = [
    'Сільпо 640 грн з картки Монобанк',
    'АЗС OKKO 1850 грн, пальне, ПриватБанк',
    'Переказ 5000 грн з ПриватБанк на Монобанк',
  ];

  constructor() {
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

  protected canClose(): boolean {
    return !!this.path() && !!this.panels;
  }

  protected closePanel(): void {
    this.panels?.close(this.path());
  }

  protected canSend(): boolean {
    return (this.draft().trim().length > 0 || this.agent.pendingAttachments().length > 0) && !this.agent.isUploading();
  }

  protected useSample(sample: string): void {
    this.draft.set(sample);
    void this.submit();
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
    this.agent.openRecord(proposal.formService, proposal.committedRecordId);
  }

  protected asColor(color: string | null | undefined): UiColor {
    return (color as UiColor) || 'slate';
  }
}
