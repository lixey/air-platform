import { DecimalPipe, NgComponentOutlet } from '@angular/common';
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
import {
  AgentChartBlockComponent,
  AgentHtmlBlockComponent,
  AgentKpiBlockComponent,
  AgentMarkdownBlockComponent,
  AgentTableBlockComponent,
} from '../agent/agent-blocks.component';
import { BadgeComponent } from '../badge/badge.component';
import { BtnComponent } from '../btn/btn.component';
import type { AgentSyncSourceDto, UiColor, UiIcon } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { NavComponent } from '../nav/nav.component';
import { OverlayRef } from '../overlay/overlay-ref';
import { parseSize } from '../panels/panel-size';
import { PanelsState } from '../panels/panels-state';
import { PANELS } from '../panels/panels-tokens';
import type { SelectOption } from '../select/select-option';
import { SelectService } from '../select/select.service';
import { fieldClasses } from '../theme/css-classes';
import { fitPromptToText, formatFullAgentDateTime, formatShortAgentDateTime } from './agent-ui-helpers';
import { type AgentChatMessage, type AgentProposal, AgentService } from './agent.service';

@Component({
  selector: 'ui-layout-agent',
  imports: [
    DecimalPipe,
    NgComponentOutlet,
    BtnComponent,
    BadgeComponent,
    NavComponent,
    UiIconDirective,
    AgentMarkdownBlockComponent,
    AgentChartBlockComponent,
    AgentTableBlockComponent,
    AgentKpiBlockComponent,
    AgentHtmlBlockComponent,
  ],
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
          <i icon="chevron-bottom" class="shrink-0 opacity-60" aria-hidden="true"></i>
        </ui-btn>
      }

      <ng-container ngProjectAs="[end]">
        <ui-btn variant="ghost" icon="reload" [title]="texts.of('AgentNewChat')" [disabled]="agent.isStreaming()" (click)="agent.reset()" />
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
        @if (msg.role === 'user') {
          <div class="ml-6 flex flex-col items-end gap-1">
            @if (msg.attachments.length > 0) {
              <div class="flex flex-wrap justify-end gap-1.5">
                @for (att of msg.attachments; track att.id) {
                  @if (isImageAttachment(att.contentType)) {
                    <a
                      [href]="att.previewUrl"
                      target="_blank"
                      rel="noopener"
                      class="block overflow-hidden rounded-md border border-line bg-surface-2"
                      [title]="att.fileName"
                    >
                      <img [src]="att.previewUrl" [alt]="att.fileName" class="h-24 max-w-36 object-cover" />
                    </a>
                  } @else {
                    <a
                      [href]="att.previewUrl"
                      target="_blank"
                      rel="noopener"
                      class="flex items-center gap-1.5 rounded-md border border-line bg-surface-2 px-2.5 py-1.5 text-xs font-medium text-ink"
                      [title]="att.fileName"
                    >
                      <i class="flex h-3.5 w-3.5 shrink-0 items-center justify-center text-muted" [icon]="'import'"></i>
                      <span class="max-w-44 truncate">{{ att.fileName }}</span>
                    </a>
                  }
                }
              </div>
            }
            @if (msg.text) {
              <div class="rounded-lg bg-active px-3 py-2 text-sm whitespace-pre-wrap text-on-active break-words">
                {{ msg.text }}
              </div>
            }
            <div class="flex items-center justify-end gap-0.5 text-[11px] text-muted">
              <span class="px-1 tabular-nums" [title]="formatFullDateTime(msg.createdAt)">
                {{ formatShortDateTime(msg.createdAt) }}
              </span>
              @if (msg.text) {
                <button
                  type="button"
                  class="flex h-6 w-6 cursor-pointer items-center justify-center rounded-md text-muted transition-colors hover:bg-hover hover:text-ink"
                  [class.bg-hover]="copiedId() === msg.id"
                  [class.text-ink]="copiedId() === msg.id"
                  [title]="copiedId() === msg.id ? texts.of('AgentCopied') : texts.of('AgentCopyMessage')"
                  [attr.aria-label]="copiedId() === msg.id ? texts.of('AgentCopied') : texts.of('AgentCopyMessage')"
                  (click)="copyMessage(msg)"
                >
                  <i class="flex scale-85 items-center justify-center" [icon]="copiedId() === msg.id ? 'checkmark' : 'copy'"></i>
                </button>
                <button
                  type="button"
                  class="flex h-6 w-6 cursor-pointer items-center justify-center rounded-md text-muted transition-colors hover:bg-hover hover:text-ink"
                  [title]="texts.of('AgentReturnToInput')"
                  [attr.aria-label]="texts.of('AgentReturnToInput')"
                  (click)="returnToInput(msg.text)"
                >
                  <i class="flex scale-85 items-center justify-center" [icon]="'reply'"></i>
                </button>
              }
              @if (!msg.syncSource) {
                <button
                  type="button"
                  class="flex h-6 w-6 cursor-pointer items-center justify-center rounded-md text-muted transition-colors hover:bg-hover hover:text-ink disabled:pointer-events-none disabled:opacity-40"
                  [title]="texts.of('AgentRetryMessage')"
                  [attr.aria-label]="texts.of('AgentRetryMessage')"
                  [disabled]="agent.isStreaming()"
                  (click)="agent.retryMessage(msg.id)"
                >
                  <i class="flex scale-85 items-center justify-center" [icon]="'refresh'"></i>
                </button>
              }
            </div>
          </div>
        } @else {
          <div class="mr-3 flex flex-col items-start gap-2 min-w-0">
            @if (msg.blocks && msg.blocks.length > 0) {
              @for (block of msg.blocks; track block.id) {
                @switch (block.kind) {
                  @case ('markdown') {
                    <ui-agent-markdown-block [content]="$any(block).content" />
                  }
                  @case ('chart') {
                    <ui-agent-chart-block [chart]="$any(block).chart" />
                  }
                  @case ('table') {
                    <ui-agent-table-block [table]="$any(block).table" />
                  }
                  @case ('kpi') {
                    <ui-agent-kpi-block [kpis]="$any(block).kpis" />
                  }
                  @case ('html') {
                    <ui-agent-html-block [html]="$any(block).html" />
                  }
                  @default {
                    @if ($any(block).component; as comp) {
                      <ng-container [ngComponentOutlet]="comp" [ngComponentOutletInputs]="{ block }" />
                    }
                  }
                }
              }
            } @else if (msg.text) {
              <ui-agent-markdown-block [content]="msg.text" />
            }

            @if (msg.statusText) {
              <div class="flex flex-wrap items-center gap-1.5 px-1 text-xs text-muted">
                <i class="flex h-3.5 w-3.5 items-center justify-center text-mark" [icon]="'spinner'"></i>
                <span>{{ msg.statusText }}</span>
                @if (msg.durationMs !== undefined && msg.durationMs > 0) {
                  <span class="tabular-nums opacity-75">· {{ formatDuration(msg.durationMs) }}</span>
                }
                @if (msg.modelName) {
                  <span class="truncate opacity-75">· {{ msg.modelName }}</span>
                }
              </div>
            }

            @if (!msg.stepsCollapsed || !isAllCommitted(msg)) {
              @for (proposal of msg.proposals; track proposal.id) {
                <div
                  class="w-full overflow-hidden rounded-lg border border-line bg-surface-2 shadow-xs transition-opacity"
                  [class.opacity-60]="proposal.status === 'superseded'"
                >
                  <div class="flex items-center justify-between gap-2 border-b border-line bg-surface-3 px-3 py-2">
                    <div class="min-w-0 flex-1">
                      <div class="flex min-w-0 items-center gap-1.5">
                        @if (proposal.stepNumber) {
                          <span
                            class="inline-flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full bg-surface px-1.5 text-[11px] font-bold tabular-nums text-mark ring-1 ring-line"
                          >
                            {{ proposal.stepNumber }}
                          </span>
                        }
                        <div class="truncate text-sm font-semibold text-ink">{{ proposal.title }}</div>
                      </div>
                      @if (proposal.subtitle) {
                        <div class="truncate text-xs text-muted" [class.pl-6]="!!proposal.stepNumber">{{ proposal.subtitle }}</div>
                      }
                    </div>
                    @if (proposal.badge) {
                      <ui-badge [label]="proposal.badge" [color]="asColor(proposal.badgeColor)" />
                    }
                  </div>

                  @if (proposal.duplicateWarning && proposal.status !== 'committed' && proposal.status !== 'superseded') {
                    <div
                      class="flex items-center justify-between gap-2 border-b border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-700 dark:text-amber-300"
                    >
                      <div class="flex min-w-0 items-start gap-1.5">
                        <i class="mt-0.5 flex h-3.5 w-3.5 shrink-0 items-center justify-center" [icon]="'alert'"></i>
                        <span class="font-medium break-words">{{ proposal.duplicateWarning }}</span>
                      </div>
                      @if (proposal.duplicateRecordId) {
                        <ui-btn
                          size="small"
                          variant="ghost"
                          color="amber"
                          icon="external"
                          [label]="texts.of('Open')"
                          (click)="openDuplicate(proposal)"
                        />
                      }
                    </div>
                  }

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
                                {{ line.qty | number: '1.0-4' }} × {{ line.price | number: '1.2-4' }}
                                @if (line.category) {
                                  <span>· {{ line.category }}</span>
                                }
                                @for (f of line.fields ?? []; track $index) {
                                  <span>· {{ f.label }}: {{ f.value }}</span>
                                }
                              </div>
                            </div>
                            <div class="shrink-0 font-medium tabular-nums text-ink">
                              {{ line.amount | number: '1.2-4' }}
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
                    } @else if (proposal.status === 'superseded') {
                      <div class="flex items-center gap-1.5 text-xs text-muted">
                        <i class="flex h-3.5 w-3.5 items-center justify-center" [icon]="'reload'"></i>
                        <span>{{ texts.of('AgentSupersededBadge') }}</span>
                      </div>
                    } @else {
                      <span class="text-xs text-muted">{{ texts.of('AgentCheckBeforeSave') }}</span>
                      <ui-btn
                        size="small"
                        variant="soft"
                        color="green"
                        icon="checkmark"
                        [label]="texts.of('AgentConfirm')"
                        [loading]="proposal.status === 'confirming'"
                        [disabled]="isBatchConfirming(msg)"
                        (click)="agent.confirmProposal(msg.id, proposal.id)"
                      />
                    }
                  </div>
                </div>
              }
            }

            @if (msg.proposals.length >= 2 && !isAllSuperseded(msg)) {
              @if (isAllCommitted(msg)) {
                <div
                  class="flex w-full items-center justify-between gap-2 rounded-lg border border-green-500/30 bg-green-500/10 px-3 py-2 text-xs font-semibold text-green-700 dark:text-green-300"
                >
                  <div class="flex min-w-0 items-center gap-1.5">
                    <i class="flex h-4 w-4 shrink-0 items-center justify-center" [icon]="'checkmark'"></i>
                    <span class="truncate">
                      {{
                        msg.batchCode
                          ? texts.of('AgentBatchSavedBadge', msg.batchCode, actionableCount(msg))
                          : texts.of('AgentAllSaved', actionableCount(msg))
                      }}
                    </span>
                  </div>
                  <ui-btn
                    size="small"
                    variant="ghost"
                    color="green"
                    [icon]="msg.stepsCollapsed ? 'chevron-bottom' : 'chevron-top'"
                    [label]="msg.stepsCollapsed ? texts.of('AgentShowSteps', actionableCount(msg)) : texts.of('AgentHideSteps')"
                    (click)="agent.toggleStepsCollapsed(msg.id)"
                  />
                </div>
              } @else if (pendingOrConfirmingCount(msg) >= 2) {
                <div class="flex w-full items-center justify-between gap-2 rounded-lg border border-line bg-surface-2 px-3 py-2 shadow-xs">
                  <span class="text-xs font-medium text-ink">
                    {{ texts.of('AgentLinkedSteps', actionableCount(msg)) }}
                  </span>
                  <ui-btn
                    size="small"
                    variant="solid"
                    color="green"
                    icon="checkmark"
                    [label]="texts.of('AgentConfirmAll', pendingOrConfirmingCount(msg))"
                    [loading]="isBatchConfirming(msg)"
                    [disabled]="agent.isStreaming()"
                    (click)="agent.confirmBatch(msg.id)"
                  />
                </div>
              }
            }

            @if (!msg.statusText && (msg.text || msg.proposals.length > 0)) {
              <div class="flex items-center gap-0.5 text-[11px] text-muted">
                @if (msg.text) {
                  <button
                    type="button"
                    class="flex h-7 w-7 cursor-pointer items-center justify-center rounded-lg text-muted transition-colors hover:bg-hover hover:text-ink"
                    [class.bg-hover]="copiedId() === msg.id"
                    [class.text-ink]="copiedId() === msg.id"
                    [title]="copiedId() === msg.id ? texts.of('AgentCopied') : texts.of('AgentCopyMessage')"
                    [attr.aria-label]="copiedId() === msg.id ? texts.of('AgentCopied') : texts.of('AgentCopyMessage')"
                    (click)="copyMessage(msg)"
                  >
                    <i class="flex scale-85 items-center justify-center" [icon]="copiedId() === msg.id ? 'checkmark' : 'copy'"></i>
                  </button>
                  <button
                    type="button"
                    class="flex h-7 w-7 cursor-pointer items-center justify-center rounded-lg text-muted transition-colors hover:bg-hover hover:text-ink"
                    [title]="texts.of('AgentReturnToInput')"
                    [attr.aria-label]="texts.of('AgentReturnToInput')"
                    (click)="returnToInput(msg.text)"
                  >
                    <i class="flex scale-85 items-center justify-center" [icon]="'reply'"></i>
                  </button>
                }
                @if (!msg.syncSource) {
                  <button
                    type="button"
                    class="flex h-7 w-7 cursor-pointer items-center justify-center rounded-lg text-muted transition-colors hover:bg-hover hover:text-ink disabled:pointer-events-none disabled:opacity-40"
                    [title]="texts.of('AgentRetryMessage')"
                    [attr.aria-label]="texts.of('AgentRetryMessage')"
                    [disabled]="agent.isStreaming()"
                    (click)="agent.retryMessage(msg.id)"
                  >
                    <i class="flex scale-85 items-center justify-center" [icon]="'refresh'"></i>
                  </button>
                }
                <span class="ml-1 tabular-nums" [title]="formatFullDateTime(msg.createdAt)">
                  {{ formatShortDateTime(msg.createdAt) }}
                </span>
                @if (msg.durationMs !== undefined && msg.durationMs > 0) {
                  <span class="flex items-center gap-1 tabular-nums">
                    <span>·</span>
                    <i class="flex scale-75 items-center justify-center opacity-75" [icon]="'clock'"></i>
                    <span>{{ formatDuration(msg.durationMs) }}</span>
                  </span>
                }
                @if (msg.modelName) {
                  <span class="flex items-center gap-1 truncate">
                    <span>·</span>
                    <span class="truncate">{{ msg.modelName }}</span>
                  </span>
                }
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
                [attr.aria-label]="texts.of('Delete')"
                (click)="agent.removePendingAttachment(att.id)"
              >
                <i class="flex items-center justify-center" [icon]="'close'"></i>
              </button>
            </div>
          }
        </div>
      }

      <input #fileInput type="file" accept="image/*" multiple class="hidden" (change)="onFilesPicked($event)" />
      <input
        #syncFileInput
        type="file"
        [accept]="pendingFileSyncSource()?.fileAccept || '.xlsx,.xls,.csv,.json'"
        class="hidden"
        (change)="onSyncFilePicked($event)"
      />

      <div class="flex items-start gap-1.5">
        <ui-btn
          size="medium"
          variant="soft"
          color="blue"
          icon="plus"
          [title]="agent.syncSources().length > 0 ? texts.of('AgentActionsTitle') : texts.of('AgentAttachTitle')"
          [loading]="agent.isUploading()"
          [disabled]="agent.isStreaming()"
          (click)="onLeftActionClick($event)"
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
  private readonly panelsState = inject(PanelsState, { optional: true });
  private readonly overlayRef = inject(OverlayRef, { optional: true });
  private readonly select = inject(SelectService);
  private readonly injector = inject(Injector);

  readonly path = input<string>('');
  protected readonly draft = signal('');
  protected readonly isDragging = signal(false);
  protected readonly copiedId = signal<string | null>(null);
  protected readonly fieldClasses = fieldClasses;
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
  private readonly promptInput = viewChild<ElementRef<HTMLTextAreaElement>>('promptInput');
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private readonly syncFileInput = viewChild<ElementRef<HTMLInputElement>>('syncFileInput');
  private readonly areaWidth = signal(0);
  protected readonly pendingFileSyncSource = signal<AgentSyncSourceDto | null>(null);
  private selectContext: 'model' | 'composer' = 'model';

  protected readonly currentModelLabel = computed(() => {
    const id = this.agent.model();
    const prov = this.agent.activeProvider();
    const list = this.agent.models();
    return (list.find(m => m.id === id && (!prov || m.provider === prov)) ?? list.find(m => m.id === id))?.name ?? id;
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
    void this.agent.ensureStatusLoaded(true);

    const subscription = this.select.committed.subscribe(options => {
      const picked = options[0];
      if (!picked) {
        return;
      }
      if (this.selectContext === 'composer') {
        if (picked.id === 0) {
          this.fileInput()?.nativeElement.click();
          return;
        }
        const src = picked.row as AgentSyncSourceDto | undefined;
        if (!src) {
          return;
        }
        if (src.acceptsFile) {
          this.pendingFileSyncSource.set(src);
          queueMicrotask(() => this.syncFileInput()?.nativeElement.click());
        } else {
          void this.agent.runSync(src);
        }
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
    this.selectContext = 'model';
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

  protected onLeftActionClick(event: Event): void {
    const sources = this.agent.syncSources();
    if (sources.length === 0) {
      this.fileInput()?.nativeElement.click();
      return;
    }

    this.selectContext = 'composer';
    const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const options: SelectOption[] = [
      {
        id: 0,
        label: this.texts.of('AgentAttachPhotoOption'),
        icon: 'photo',
      },
      ...sources.map((src, idx) => ({
        id: idx + 1,
        label: src.label,
        subtitle: src.subtitle ?? undefined,
        icon: ((src.icon as UiIcon) || (src.acceptsFile ? 'import' : 'reload')) as UiIcon,
        row: src,
      })),
    ];

    this.select.open({ model: { label: this.texts.of('AgentActionsTitle'), options }, picked: [] }, anchor, this.injector);
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

  protected async onSyncFilePicked(event: Event): Promise<void> {
    const inputEl = event.target as HTMLInputElement;
    const file = inputEl.files?.[0];
    const src = this.pendingFileSyncSource();
    inputEl.value = '';
    this.pendingFileSyncSource.set(null);

    if (!file || !src) {
      return;
    }

    const uploaded = await this.agent.uploadSingleFile(file);
    if (uploaded) {
      await this.agent.runSync(src, [uploaded]);
    }
  }

  protected isImageAttachment(contentType: string | null | undefined): boolean {
    return !contentType || contentType.startsWith('image/');
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

  protected openCommitted(proposal: AgentProposal): void {
    this.agent.openRecord(proposal);
  }

  protected openDuplicate(proposal: AgentProposal): void {
    if (proposal.duplicateRecordId) {
      this.agent.openRecord(proposal, proposal.duplicateRecordId, proposal.duplicateFormService ?? undefined);
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
    if (!text) {
      return;
    }
    this.draft.set(text);
    queueMicrotask(() => {
      const area = this.promptInput()?.nativeElement;
      if (area) {
        fitPromptToText(area);
        area.focus();
        area.setSelectionRange(area.value.length, area.value.length);
      }
    });
  }

  protected readonly formatShortDateTime = formatShortAgentDateTime;
  protected readonly formatFullDateTime = formatFullAgentDateTime;

  protected formatDuration(durationMs: number): string {
    const sec = (durationMs / 1000).toFixed(1);
    return this.texts.of('AgentDurationSec', sec);
  }

  protected asColor(color: string | null | undefined): UiColor {
    return (color as UiColor) || 'slate';
  }

  protected actionableCount(msg: AgentChatMessage): number {
    return msg.proposals.filter(p => p.status !== 'superseded').length;
  }

  protected pendingOrConfirmingCount(msg: AgentChatMessage): number {
    return msg.proposals.filter(p => p.status === 'pending' || p.status === 'confirming').length;
  }

  protected isBatchConfirming(msg: AgentChatMessage): boolean {
    return msg.proposals.filter(p => p.status === 'confirming').length >= 2;
  }

  protected isAllCommitted(msg: AgentChatMessage): boolean {
    const active = msg.proposals.filter(p => p.status !== 'superseded');
    return active.length >= 2 && active.every(p => p.status === 'committed');
  }

  protected isAllSuperseded(msg: AgentChatMessage): boolean {
    return msg.proposals.length > 0 && msg.proposals.every(p => p.status === 'superseded');
  }
}
