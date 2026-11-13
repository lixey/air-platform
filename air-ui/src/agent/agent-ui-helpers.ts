import type { UiColor } from '../core/generated/contracts';
import { GridService } from '../grid/grid.service';
import type { OverlayService } from '../overlay/overlay.service';
import { UI_COLORS } from '../theme/ui-color';
import type { AgentProposal } from './agent-models';

const MAX_PROMPT_HEIGHT = 160;

export function asAgentUiColor(color: string | null | undefined): UiColor | null {
  if (!color) {
    return null;
  }
  const lower = color.toLowerCase();
  return UI_COLORS.find(c => c === lower) ?? null;
}

export function fitPromptToText(area: HTMLTextAreaElement): void {
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

export function formatShortAgentDateTime(date: Date): string {
  const d = date instanceof Date ? date : new Date(date);
  const now = new Date();
  const sameDay = d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate();
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  if (sameDay) {
    return `${hh}:${mm}`;
  }
  const day = String(d.getDate()).padStart(2, '0');
  const mon = String(d.getMonth() + 1).padStart(2, '0');
  return `${day}.${mon} ${hh}:${mm}`;
}

export function formatFullAgentDateTime(date: Date): string {
  const d = date instanceof Date ? date : new Date(date);
  const day = String(d.getDate()).padStart(2, '0');
  const mon = String(d.getMonth() + 1).padStart(2, '0');
  const yr = d.getFullYear();
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  const ss = String(d.getSeconds()).padStart(2, '0');
  return `${day}.${mon}.${yr} ${hh}:${mm}:${ss}`;
}

export function openAgentProposalRecord(
  overlay: OverlayService,
  proposal: AgentProposal,
  explicitId?: number,
  explicitService?: string,
): void {
  const formService = explicitService ?? proposal.formService ?? proposal.service;
  const recordId = explicitId ?? proposal.committedRecordId ?? proposal.recordId;
  if (!formService || !recordId) {
    return;
  }
  const baseOverlay = proposal.overlay ?? {
    key: formService,
    presentation: 'drawer' as const,
    closesOnEscape: true,
  };
  const ref = overlay.open({
    ...baseOverlay,
    screen: {
      ...(baseOverlay.screen ?? {}),
      service: formService,
      params: { id: recordId },
    },
  });
  ref.closed.subscribe(result => {
    if (result !== undefined) {
      GridService.reloadServices(proposal.service ? [proposal.service] : undefined);
    }
  });
}
