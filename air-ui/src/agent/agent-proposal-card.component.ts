import { DecimalPipe } from '@angular/common';
import { Component, inject, input, output } from '@angular/core';
import { BadgeComponent } from '../badge/badge.component';
import { BtnComponent } from '../btn/btn.component';
import type { UiColor } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { asAgentUiColor } from './agent-ui-helpers';
import type { AgentProposal, AgentProposalLine } from './agent.service';

@Component({
  selector: 'ui-agent-proposal-card',
  imports: [DecimalPipe, BadgeComponent, BtnComponent, UiIconDirective],
  template: `
    @let p = proposal();
    <div
      class="w-full overflow-hidden rounded-lg border border-line bg-surface-2 shadow-xs transition-opacity"
      [class.opacity-60]="p.status === 'superseded'"
    >
      <div class="flex items-center justify-between gap-2 border-b border-line bg-surface-3 px-3 py-2">
        <div class="min-w-0 flex-1">
          <div class="flex min-w-0 items-center gap-1.5">
            @if (p.stepNumber) {
              <span
                class="inline-flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full bg-surface px-1.5 text-[11px] font-bold tabular-nums text-mark ring-1 ring-line"
              >
                {{ p.stepNumber }}
              </span>
            }
            <div class="truncate text-sm font-semibold text-ink">{{ p.title }}</div>
          </div>
          @if (p.subtitle) {
            <div class="truncate text-xs text-muted" [class.pl-6]="!!p.stepNumber">{{ p.subtitle }}</div>
          }
        </div>
        @if (p.badge) {
          <ui-badge [label]="p.badge" [color]="asColor(p.badgeColor)" />
        }
      </div>

      @if (p.duplicateWarning && p.status !== 'committed' && p.status !== 'superseded') {
        <div
          class="flex items-center justify-between gap-2 border-b border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-700 dark:text-amber-300"
        >
          <div class="flex min-w-0 items-start gap-1.5">
            <i class="mt-0.5 flex h-3.5 w-3.5 shrink-0 items-center justify-center" [icon]="'alert'"></i>
            <span class="font-medium break-words">{{ p.duplicateWarning }}</span>
          </div>
          @if (p.duplicateRecordId) {
            <ui-btn size="small" variant="ghost" color="amber" icon="external" [label]="texts.of('Open')" (click)="openDuplicate.emit(p)" />
          }
        </div>
      }

      <div class="divide-y divide-line/60 px-3 py-1.5 text-xs">
        @for (field of p.fields; track field.label) {
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

      @if (p.lines.length > 0) {
        <div class="border-t border-line bg-surface px-3 py-2">
          <div class="mb-1.5 text-[11px] font-semibold tracking-wide uppercase text-muted">
            {{ p.linesLabel || texts.of('AgentLinesTitle', p.lines.length) }}
          </div>
          <div class="max-h-44 space-y-1 overflow-y-auto pr-1 text-xs">
            @for (line of p.lines; track $index) {
              <div class="flex items-start justify-between gap-2 border-b border-line/40 pb-1 last:border-b-0 last:pb-0">
                <div class="min-w-0 flex-1">
                  <div class="truncate text-ink" [title]="line.name">{{ line.name }}</div>
                  @if (hasLineDetails(line)) {
                    <div class="flex flex-wrap items-center gap-x-1 text-[11px] text-muted">
                      @if (line.qty !== null && line.qty !== undefined && line.price !== null && line.price !== undefined) {
                        <span class="tabular-nums">{{ line.qty | number: '1.0-4' }} × {{ line.price | number: '1.2-4' }}</span>
                      } @else if (line.qty !== null && line.qty !== undefined) {
                        <span class="tabular-nums">× {{ line.qty | number: '1.0-4' }}</span>
                      } @else if (line.price !== null && line.price !== undefined) {
                        <span class="tabular-nums">{{ line.price | number: '1.2-4' }}</span>
                      }
                      @if (line.category) {
                        @if ((line.qty !== null && line.qty !== undefined) || (line.price !== null && line.price !== undefined)) {
                          <span>·</span>
                        }
                        <span>{{ line.category }}</span>
                      }
                      @for (f of line.fields ?? []; track $index; let firstField = $first) {
                        @if (
                          !firstField ||
                          !!line.category ||
                          (line.qty !== null && line.qty !== undefined) ||
                          (line.price !== null && line.price !== undefined)
                        ) {
                          <span>·</span>
                        }
                        <span>{{ f.label }}: {{ f.value }}</span>
                      }
                    </div>
                  }
                </div>
                @if (line.amount !== null && line.amount !== undefined) {
                  <div class="shrink-0 font-medium tabular-nums text-ink">
                    {{ line.amount | number: '1.2-4' }}
                  </div>
                }
              </div>
            }
          </div>
        </div>
      }

      @if (p.attachmentIds.length > 0) {
        <div class="flex items-center gap-1.5 border-t border-line bg-surface-3/50 px-3 py-1.5 text-xs text-muted">
          <i class="flex h-3.5 w-3.5 items-center justify-center" [icon]="'photo'"></i>
          <span>{{ texts.of('AgentPhotoWillAttach') }}</span>
        </div>
      }

      <div class="flex items-center justify-between gap-2 border-t border-line bg-surface-3 px-3 py-2">
        @if (p.status === 'committed') {
          <div class="flex items-center gap-1.5 text-xs font-semibold text-green-600 dark:text-green-400">
            <i class="flex h-4 w-4 items-center justify-center" [icon]="'checkmark'"></i>
            <span>{{ texts.of('AgentSaved', p.committedLabel ?? '') }}</span>
          </div>
          <ui-btn size="small" variant="ghost" color="blue" icon="external" [label]="texts.of('Open')" (click)="openCommitted.emit(p)" />
        } @else if (p.status === 'superseded') {
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
            [loading]="p.status === 'confirming'"
            [disabled]="batchConfirming()"
            (click)="confirm.emit(p.id)"
          />
        }
      </div>
    </div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentProposalCardComponent {
  protected readonly texts = inject(TextsService);

  readonly proposal = input.required<AgentProposal>();
  readonly batchConfirming = input(false);

  readonly confirm = output<string>();
  readonly openCommitted = output<AgentProposal>();
  readonly openDuplicate = output<AgentProposal>();

  protected asColor(color: string | null | undefined): UiColor {
    return asAgentUiColor(color) ?? 'slate';
  }

  protected hasLineDetails(line: AgentProposalLine): boolean {
    return (
      (line.qty !== null && line.qty !== undefined) ||
      (line.price !== null && line.price !== undefined) ||
      !!line.category ||
      !!line.fields?.length
    );
  }
}
