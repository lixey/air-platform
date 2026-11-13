import { HttpErrorResponse } from '@angular/common/http';
import type {
  AgentAttachmentDto,
  AgentModelDto,
  AgentProposalChildColumnDto,
  AgentProposalChildGroupDto,
  AgentProposalChildRowDto,
  AgentProposalDto,
  AgentProposalFieldDto,
  AgentSavedMemoryDto,
  AgentSyncSourceDto,
} from '../core/generated/contracts';
import { newUuid } from '../core/uuid';
import type { ImageRules } from '../file/file-optimize';
import type { AgentContentBlock } from './agent-block-models';

export type AgentAttachment = AgentAttachmentDto;
export type AgentProposalField = AgentProposalFieldDto;
export type AgentProposalChildColumn = AgentProposalChildColumnDto;
export type AgentProposalChildRow = AgentProposalChildRowDto;
export type AgentProposalChildGroup = AgentProposalChildGroupDto;
export type AgentProposal = AgentProposalDto;
export type AgentModel = AgentModelDto;

export interface AgentMemoryItem extends AgentSavedMemoryDto {
  readonly confirming?: boolean;
  readonly dismissed?: boolean;
  readonly undoing?: boolean;
  readonly undone?: boolean;
}

export interface AgentChatMessage {
  readonly id: string;
  readonly role: 'user' | 'assistant';
  text: string;
  readonly blocks?: readonly AgentContentBlock[];
  statusText?: string | null;
  readonly attachments: readonly AgentAttachment[];
  readonly proposals: AgentProposal[];
  readonly memories: readonly AgentMemoryItem[];
  readonly createdAt: Date;
  readonly durationMs?: number;
  readonly modelId?: string;
  readonly modelName?: string;
  readonly promptText?: string;
  readonly promptAttachments?: readonly AgentAttachment[];
  readonly syncSource?: AgentSyncSourceDto;
  readonly batchCode?: string | null;
  readonly stepsCollapsed?: boolean;
}

export const AGENT_IMAGE_RULES: ImageRules = {
  images: 'optimize',
  imageMaxSide: 2048,
  imageQuality: 0.85,
  accept: ['image/jpeg', 'image/png'],
};

export function createSessionId(): string {
  return newUuid().replace(/-/g, '');
}

export function extractAgentErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof HttpErrorResponse && err.error?.title) {
    return String(err.error.title);
  }
  if (err instanceof Error && err.message) {
    return err.message;
  }
  return fallback;
}

export function resolveAgentTurnModel(
  modelsList: readonly AgentModelDto[],
  currentModel: string,
  activeProvider: string,
  userPickedModel: boolean,
  attachments: readonly AgentAttachment[],
): { activeModelObj: AgentModelDto | undefined; hasImageAttachment: boolean } {
  const hasImageAttachment = attachments.some(a => !a.contentType || a.contentType.startsWith('image/'));
  const byCurrent = () =>
    modelsList.find(m => m.id === currentModel && m.provider === activeProvider) ?? modelsList.find(m => m.id === currentModel);
  const activeModelObj = hasImageAttachment && !userPickedModel ? (modelsList.find(m => m.isForPhotos) ?? byCurrent()) : byCurrent();
  return { activeModelObj, hasImageAttachment };
}

export function resolveAgentRetryPlan(
  list: readonly AgentChatMessage[],
  messageId: string,
): { removeIds: Set<string>; promptText: string; promptAttachments: readonly AgentAttachment[] } | null {
  const idx = list.findIndex(m => m.id === messageId);
  if (idx < 0) {
    return null;
  }
  const target = list[idx];
  if (target.role === 'assistant') {
    const prevUser = idx > 0 && list[idx - 1]?.role === 'user' ? list[idx - 1] : null;
    if (target.syncSource || prevUser?.syncSource) {
      return null;
    }
    const promptText = target.promptText ?? prevUser?.text ?? '';
    const promptAttachments = target.promptAttachments ?? prevUser?.attachments ?? [];
    if (!promptText && promptAttachments.length === 0) {
      return null;
    }
    return { removeIds: new Set<string>([target.id, ...(prevUser ? [prevUser.id] : [])]), promptText, promptAttachments };
  }
  if (target.syncSource || (!target.text && target.attachments.length === 0)) {
    return null;
  }
  const nextAssistant = idx + 1 < list.length && list[idx + 1]?.role === 'assistant' ? list[idx + 1] : null;
  const removeIds = nextAssistant && idx + 1 === list.length - 1 ? new Set<string>([target.id, nextAssistant.id]) : new Set<string>();
  return { removeIds, promptText: target.text, promptAttachments: target.attachments };
}
