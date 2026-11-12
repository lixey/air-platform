import { Component, inject, input, output } from '@angular/core';
import { BadgeComponent } from '../badge/badge.component';
import { BtnComponent } from '../btn/btn.component';
import type { UiColor } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import type { AgentProposal, AgentProposalChildGroup } from './agent-models';
import { asAgentUiColor } from './agent-ui-helpers';

@Component({
  selector: 'ui-agent-proposal-card',
  imports: [BadgeComponent, BtnComponent, UiIconDirective],
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
          class="hue-amber flex items-center justify-between gap-2 border-b border-(--btn-edge) bg-(--btn-ground) px-3 py-2 text-xs text-(--btn-ink)"
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

      @if (p.fields.length > 0) {
        <div class="divide-y divide-line/60 px-3 py-1.5 text-xs">
          @for (field of p.fields; track field.label) {
            <div class="flex items-center justify-between gap-2 py-1">
              <span class="text-muted">{{ field.label }}</span>
              <span class="flex items-center gap-1.5 text-right font-medium text-ink">
                <span>{{ field.value }}</span>
                @if (field.badge) {
                  <ui-badge [label]="field.badge" [color]="asColor(field.color)" />
                }
              </span>
            </div>
          }
        </div>
      }

      @for (group of p.children; track group.childKey) {
        @if (group.rows.length > 0) {
          <div class="border-t border-line bg-surface px-3 py-2">
            <div class="mb-1.5 flex items-center justify-between gap-2">
              <span class="text-[11px] font-semibold tracking-wide uppercase text-muted">
                {{ groupTitle(group) }}
              </span>
              @if (p.operation === 'update' || group.mode === 'replace') {
                <ui-badge
                  [label]="group.mode === 'replace' ? texts.of('AgentChildModeReplace') : texts.of('AgentChildModeAppend')"
                  [color]="group.mode === 'replace' ? 'amber' : 'slate'"
                />
              }
            </div>
            <div class="max-h-48 overflow-x-auto overflow-y-auto">
              <table class="w-full border-collapse text-xs">
                @if (group.columns.length > 0) {
                  <thead>
                    <tr class="border-b border-line/60 text-[11px] text-muted">
                      @for (col of group.columns; track col.key) {
                        <th class="py-1 pr-2 font-medium last:pr-0" [class.text-left]="!col.isNumeric" [class.text-right]="col.isNumeric">
                          {{ col.label }}
                        </th>
                      }
                    </tr>
                  </thead>
                }
                <tbody class="divide-y divide-line/40">
                  @for (row of group.rows; track $index) {
                    <tr>
                      @for (col of group.columns; track col.key) {
                        <td
                          class="py-1 pr-2 text-ink last:pr-0"
                          [class.text-right]="col.isNumeric"
                          [class.tabular-nums]="col.isNumeric"
                          [class.font-medium]="col.isNumeric && $last"
                        >
                          {{ row.cells[col.key] ?? '—' }}
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        }
      }

      @if (p.attachmentIds.length > 0) {
        <div class="flex items-center gap-1.5 border-t border-line bg-surface-3/50 px-3 py-1.5 text-xs text-muted">
          <i class="flex h-3.5 w-3.5 items-center justify-center" [icon]="'photo'"></i>
          <span>{{ texts.of('AgentPhotoWillAttach') }}</span>
        </div>
      }

      <div class="flex items-center justify-between gap-2 border-t border-line bg-surface-3 px-3 py-2">
        @if (p.status === 'committed') {
          <div class="hue-green flex items-center gap-1.5 text-xs font-semibold text-(--hue-ink)">
            <i class="flex h-4 w-4 items-center justify-center" [icon]="'checkmark'"></i>
            <span>{{ texts.of('AgentSaved', p.committedLabel ?? '') }}</span>
          </div>
          <ui-btn size="small" variant="ghost" color="blue" icon="external" [label]="texts.of('Open')" (click)="openCommitted.emit(p)" />
        } @else if (p.status === 'superseded') {
          <div class="flex items-center gap-1.5 text-xs text-muted">
            <i class="flex h-3.5 w-3.5 items-center justify-center" [icon]="'refresh'"></i>
            <span>{{ texts.of('AgentSupersededBadge') }}</span>
          </div>
        } @else {
          <span class="text-xs text-muted">
            {{ p.operation === 'move-status' ? texts.of('AgentCheckBeforeMove') : texts.of('AgentCheckBeforeSave') }}
          </span>
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

  protected groupTitle(group: AgentProposalChildGroup): string {
    return group.title ? `${group.title} (${group.rows.length})` : this.texts.of('AgentLinesTitle', group.rows.length);
  }
}
