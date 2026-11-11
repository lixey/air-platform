import { NgComponentOutlet } from '@angular/common';
import { Component, inject, input, output } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { AgentChartBlockComponent } from './agent-chart-block.component';
import { AgentHtmlBlockComponent } from './agent-html-block.component';
import { AgentKpiBlockComponent } from './agent-kpi-block.component';
import { AgentMarkdownBlockComponent } from './agent-markdown-block.component';
import { AgentProposalCardComponent } from './agent-proposal-card.component';
import { AgentTableBlockComponent } from './agent-table-block.component';
import { formatFullAgentDateTime, formatShortAgentDateTime } from './agent-ui-helpers';
import { type AgentChatMessage, type AgentProposal, AgentService } from './agent.service';

@Component({
  selector: 'ui-agent-message-item',
  imports: [
    NgComponentOutlet,
    BtnComponent,
    UiIconDirective,
    AgentMarkdownBlockComponent,
    AgentChartBlockComponent,
    AgentTableBlockComponent,
    AgentKpiBlockComponent,
    AgentHtmlBlockComponent,
    AgentProposalCardComponent,
  ],
  template: `
    @let m = msg();
    @if (m.role === 'user') {
      <div class="ml-6 flex flex-col items-end gap-1">
        @if (m.attachments.length > 0) {
          <div class="flex flex-wrap justify-end gap-1.5">
            @for (att of m.attachments; track att.id) {
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
        @if (m.text) {
          <div class="rounded-lg bg-active px-3 py-2 text-sm whitespace-pre-wrap text-on-active break-words">
            {{ m.text }}
          </div>
        }
        <div class="flex items-center justify-end gap-0.5 text-[11px] text-muted">
          <span class="px-1 tabular-nums" [title]="formatFullDateTime(m.createdAt)">
            {{ formatShortDateTime(m.createdAt) }}
          </span>
          @if (m.text) {
            <button
              type="button"
              class="flex h-6 w-6 cursor-pointer items-center justify-center rounded-md text-muted transition-colors hover:bg-hover hover:text-ink"
              [class.bg-hover]="copied()"
              [class.text-ink]="copied()"
              [title]="copied() ? texts.of('AgentCopied') : texts.of('AgentCopyMessage')"
              [attr.aria-label]="copied() ? texts.of('AgentCopied') : texts.of('AgentCopyMessage')"
              (click)="copy.emit(m)"
            >
              <i class="flex scale-85 items-center justify-center" [icon]="copied() ? 'checkmark' : 'copy'"></i>
            </button>
            @if (!m.syncSource || m.promptText) {
              <button
                type="button"
                class="flex h-6 w-6 cursor-pointer items-center justify-center rounded-md text-muted transition-colors hover:bg-hover hover:text-ink"
                [title]="texts.of('AgentReturnToInput')"
                [attr.aria-label]="texts.of('AgentReturnToInput')"
                (click)="returnToInput.emit(m.syncSource ? (m.promptText ?? '') : m.text)"
              >
                <i class="flex scale-85 items-center justify-center" [icon]="'reply'"></i>
              </button>
            }
          }
          @if (!m.syncSource) {
            <button
              type="button"
              class="flex h-6 w-6 cursor-pointer items-center justify-center rounded-md text-muted transition-colors hover:bg-hover hover:text-ink disabled:pointer-events-none disabled:opacity-40"
              [title]="texts.of('AgentRetryMessage')"
              [attr.aria-label]="texts.of('AgentRetryMessage')"
              [disabled]="agent.isStreaming()"
              (click)="agent.retryMessage(m.id)"
            >
              <i class="flex scale-85 items-center justify-center" [icon]="'refresh'"></i>
            </button>
          }
        </div>
      </div>
    } @else {
      <div class="mr-3 flex flex-col items-start gap-2 min-w-0">
        @if (m.blocks && m.blocks.length > 0) {
          @for (block of m.blocks; track block.id) {
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
        } @else if (m.text) {
          <ui-agent-markdown-block [content]="m.text" />
        }

        @if (m.statusText) {
          <div class="flex flex-wrap items-center gap-1.5 px-1 text-xs text-muted">
            <i class="flex h-3.5 w-3.5 items-center justify-center text-mark" [icon]="'spinner'"></i>
            <span>{{ m.statusText }}</span>
            @if (m.durationMs !== undefined && m.durationMs > 0) {
              <span class="tabular-nums opacity-75">· {{ formatDuration(m.durationMs) }}</span>
            }
            @if (m.modelName) {
              <span class="truncate opacity-75">· {{ m.modelName }}</span>
            }
          </div>
        }

        @if (!m.stepsCollapsed || !isAllCommitted(m)) {
          @for (proposal of m.proposals; track proposal.id) {
            <ui-agent-proposal-card
              [proposal]="proposal"
              [batchConfirming]="isBatchConfirming(m)"
              (confirm)="agent.confirmProposal(m.id, $event)"
              (openCommitted)="openCommitted($event)"
              (openDuplicate)="openDuplicate($event)"
            />
          }
        }

        @if (m.proposals.length >= 2 && !isAllSuperseded(m)) {
          @if (isAllCommitted(m)) {
            <div
              class="flex w-full items-center justify-between gap-2 rounded-lg border border-green-500/30 bg-green-500/10 px-3 py-2 text-xs font-semibold text-green-700 dark:text-green-300"
            >
              <div class="flex min-w-0 items-center gap-1.5">
                <i class="flex h-4 w-4 shrink-0 items-center justify-center" [icon]="'checkmark'"></i>
                <span class="truncate">
                  {{
                    m.batchCode
                      ? texts.of('AgentBatchSavedBadge', m.batchCode, actionableCount(m))
                      : texts.of('AgentAllSaved', actionableCount(m))
                  }}
                </span>
              </div>
              <ui-btn
                size="small"
                variant="ghost"
                color="green"
                [icon]="m.stepsCollapsed ? 'chevron-bottom' : 'chevron-top'"
                [label]="m.stepsCollapsed ? texts.of('AgentShowSteps', actionableCount(m)) : texts.of('AgentHideSteps')"
                (click)="agent.toggleStepsCollapsed(m.id)"
              />
            </div>
          } @else if (pendingOrConfirmingCount(m) >= 2) {
            <div class="flex w-full items-center justify-between gap-2 rounded-lg border border-line bg-surface-2 px-3 py-2 shadow-xs">
              <span class="text-xs font-medium text-ink">
                {{ texts.of('AgentLinkedSteps', actionableCount(m)) }}
              </span>
              <ui-btn
                size="small"
                variant="solid"
                color="green"
                icon="checkmark"
                [label]="texts.of('AgentConfirmAll', pendingOrConfirmingCount(m))"
                [loading]="isBatchConfirming(m)"
                [disabled]="agent.isStreaming()"
                (click)="agent.confirmBatch(m.id)"
              />
            </div>
          }
        }

        @if (!m.statusText && (m.text || m.proposals.length > 0)) {
          <div class="flex items-center gap-0.5 text-[11px] text-muted">
            @if (m.text) {
              <button
                type="button"
                class="flex h-7 w-7 cursor-pointer items-center justify-center rounded-lg text-muted transition-colors hover:bg-hover hover:text-ink"
                [class.bg-hover]="copied()"
                [class.text-ink]="copied()"
                [title]="copied() ? texts.of('AgentCopied') : texts.of('AgentCopyMessage')"
                [attr.aria-label]="copied() ? texts.of('AgentCopied') : texts.of('AgentCopyMessage')"
                (click)="copy.emit(m)"
              >
                <i class="flex scale-85 items-center justify-center" [icon]="copied() ? 'checkmark' : 'copy'"></i>
              </button>
              <button
                type="button"
                class="flex h-7 w-7 cursor-pointer items-center justify-center rounded-lg text-muted transition-colors hover:bg-hover hover:text-ink"
                [title]="texts.of('AgentReturnToInput')"
                [attr.aria-label]="texts.of('AgentReturnToInput')"
                (click)="returnToInput.emit(m.text)"
              >
                <i class="flex scale-85 items-center justify-center" [icon]="'reply'"></i>
              </button>
            }
            @if (!m.syncSource) {
              <button
                type="button"
                class="flex h-7 w-7 cursor-pointer items-center justify-center rounded-lg text-muted transition-colors hover:bg-hover hover:text-ink disabled:pointer-events-none disabled:opacity-40"
                [title]="texts.of('AgentRetryMessage')"
                [attr.aria-label]="texts.of('AgentRetryMessage')"
                [disabled]="agent.isStreaming()"
                (click)="agent.retryMessage(m.id)"
              >
                <i class="flex scale-85 items-center justify-center" [icon]="'refresh'"></i>
              </button>
            }
            <span class="ml-1 tabular-nums" [title]="formatFullDateTime(m.createdAt)">
              {{ formatShortDateTime(m.createdAt) }}
            </span>
            @if (m.durationMs !== undefined && m.durationMs > 0) {
              <span class="flex items-center gap-1 tabular-nums">
                <span>·</span>
                <i class="flex scale-75 items-center justify-center opacity-75" [icon]="'clock'"></i>
                <span>{{ formatDuration(m.durationMs) }}</span>
              </span>
            }
            @if (m.modelName) {
              <span class="flex items-center gap-1 truncate">
                <span>·</span>
                <span class="truncate">{{ m.modelName }}</span>
              </span>
            }
          </div>
        }
      </div>
    }
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentMessageItemComponent {
  protected readonly agent = inject(AgentService);
  protected readonly texts = inject(TextsService);

  readonly msg = input.required<AgentChatMessage>();
  readonly copied = input(false);

  readonly copy = output<AgentChatMessage>();
  readonly returnToInput = output<string>();

  protected readonly formatShortDateTime = formatShortAgentDateTime;
  protected readonly formatFullDateTime = formatFullAgentDateTime;

  protected isImageAttachment(contentType: string | null | undefined): boolean {
    return !contentType || contentType.startsWith('image/');
  }

  protected formatDuration(durationMs: number): string {
    const sec = (durationMs / 1000).toFixed(1);
    return this.texts.of('AgentDurationSec', sec);
  }

  protected openCommitted(proposal: AgentProposal): void {
    this.agent.openRecord(proposal);
  }

  protected openDuplicate(proposal: AgentProposal): void {
    if (proposal.duplicateRecordId) {
      this.agent.openRecord(proposal, proposal.duplicateRecordId, proposal.duplicateFormService ?? undefined);
    }
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
