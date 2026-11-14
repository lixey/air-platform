import { firstValueFrom } from 'rxjs';
import type { AlertService } from '../app/alert/alert.service';
import { withIdempotencyKey } from '../app/auth/credentials.interceptor';
import type { AnswerOf, Api, ApiRoute, RequestOf } from '../core/api';
import type { AgentCommitResultDto, PlatformEndpoints } from '../core/generated/contracts';
import { isRecord } from '../core/json';
import { classifyMutationFailure } from '../core/mutation-failure';
import type { TextsService } from '../core/texts.service';
import type { UncertainIdempotency } from '../core/uncertain-idempotency';
import { stableJson } from '../form/form-value';
import { GridService } from '../grid/grid.service';
import { type AgentChatMessage, type AgentProposal, extractAgentErrorMessage } from './agent-models';

export interface AgentMutationHost {
  readonly api: Api;
  readonly alert: AlertService;
  readonly texts: TextsService;
  readonly uncertainKeys: UncertainIdempotency;
  readonly getMessages: () => readonly AgentChatMessage[];
  readonly updateMessages: (updater: (list: readonly AgentChatMessage[]) => readonly AgentChatMessage[]) => void;
  readonly patchAssistant: (messageId: string, updater: (msg: AgentChatMessage) => AgentChatMessage) => void;
  readonly updateProposal: (messageId: string, proposalId: string, updater: (proposal: AgentProposal) => AgentProposal) => void;
}

async function postWithIdempotency<K extends ApiRoute>(
  host: AgentMutationHost,
  route: K,
  body: RequestOf<PlatformEndpoints, K>,
): Promise<AnswerOf<PlatformEndpoints, K>> {
  const fingerprint = `${route}\n${stableJson(body)}`;
  const key = host.uncertainKeys.take(fingerprint);
  try {
    const answer = await firstValueFrom(host.api.post(route, body, withIdempotencyKey(key)));
    if (isRecord(answer) && answer['outcome'] === 'unknown') {
      host.uncertainKeys.keep(fingerprint, key);
    }
    return answer;
  } catch (error: unknown) {
    if (classifyMutationFailure(error).outcome === 'unknown') {
      host.uncertainKeys.keep(fingerprint, key);
    }
    throw error;
  }
}

function applyCommitResult(host: AgentMutationHost, result: AgentCommitResultDto): void {
  if (result.warnings?.length) {
    host.alert.warnings(result.warnings, result.recordLabel);
  }
  for (const extra of result.alsoCommitted ?? []) {
    if (extra.warnings?.length) {
      host.alert.warnings(extra.warnings, extra.recordLabel);
    }
  }
  host.updateMessages(list =>
    list.map(m => ({
      ...m,
      proposals: m.proposals.map(p =>
        p.id === result.proposalId
          ? {
              ...p,
              status: 'committed',
              committedRecordId: result.recordId,
              committedLabel: result.recordLabel,
              formService: result.formService,
            }
          : p,
      ),
    })),
  );
}

export async function confirmAgentProposal(host: AgentMutationHost, messageId: string, proposalId: string): Promise<void> {
  host.updateProposal(messageId, proposalId, p => ({ ...p, status: 'confirming' }));
  try {
    const committed = await postWithIdempotency(host, 'agent/confirm-proposal', { proposalId });
    applyCommitResult(host, committed);
    for (const extra of committed.alsoCommitted ?? []) {
      applyCommitResult(host, extra);
    }
    if (committed.batchCode) {
      host.patchAssistant(messageId, m => ({ ...m, batchCode: committed.batchCode }));
    }
    GridService.reloadServices(committed.refreshServices);
  } catch (err) {
    host.updateProposal(messageId, proposalId, p => ({ ...p, status: 'pending' }));
    host.alert.error(extractAgentErrorMessage(err, host.texts.of('AgentErrorSave')));
  }
}

export async function confirmAgentBatch(host: AgentMutationHost, messageId: string): Promise<void> {
  const msg = host.getMessages().find(m => m.id === messageId);
  const pendingIds = msg?.proposals.filter(p => p.status === 'pending').map(p => p.id) ?? [];
  if (pendingIds.length === 0) {
    return;
  }
  const idSet = new Set(pendingIds);
  host.patchAssistant(messageId, m => ({
    ...m,
    proposals: m.proposals.map(p => (idSet.has(p.id) ? { ...p, status: 'confirming' } : p)),
  }));
  try {
    const batchRes = await postWithIdempotency(host, 'agent/confirm-batch', {
      proposalIds: pendingIds,
    });
    for (const item of batchRes.committed) {
      applyCommitResult(host, item);
    }
    host.patchAssistant(messageId, m => ({
      ...m,
      batchCode: batchRes.batchCode ?? m.batchCode ?? null,
      stepsCollapsed: batchRes.committed.length >= 3,
    }));
    GridService.reloadServices(batchRes.refreshServices);
  } catch (err) {
    host.patchAssistant(messageId, m => ({
      ...m,
      proposals: m.proposals.map(p => (idSet.has(p.id) && p.status === 'confirming' ? { ...p, status: 'pending' } : p)),
    }));
    host.alert.error(extractAgentErrorMessage(err, host.texts.of('AgentErrorSave')));
  }
}

export async function confirmAgentMemory(host: AgentMutationHost, messageId: string, pendingToken: string): Promise<void> {
  host.patchAssistant(messageId, m => ({
    ...m,
    memories: m.memories.map(mem => (mem.pendingToken === pendingToken ? { ...mem, confirming: true } : mem)),
  }));
  try {
    const saved = await postWithIdempotency(host, 'agent/confirm-memory', { pendingToken });
    host.patchAssistant(messageId, m => ({
      ...m,
      memories: m.memories.map(mem =>
        mem.pendingToken === pendingToken ? { ...saved, status: 'saved', pendingToken: undefined, confirming: false } : mem,
      ),
    }));
    GridService.reloadServices(['app/agent-memory']);
  } catch (err) {
    const isGone = (err as { status?: number } | null)?.status === 404;
    host.patchAssistant(messageId, m => ({
      ...m,
      memories: m.memories.map(mem =>
        mem.pendingToken === pendingToken ? { ...mem, confirming: false, dismissed: isGone ? true : mem.dismissed } : mem,
      ),
    }));
    host.alert.error(extractAgentErrorMessage(err, host.texts.of('RequestFailed')));
  }
}

export function dismissAgentMemory(host: AgentMutationHost, messageId: string, pendingToken: string): void {
  host.patchAssistant(messageId, m => ({
    ...m,
    memories: m.memories.map(mem => (mem.pendingToken === pendingToken ? { ...mem, dismissed: true } : mem)),
  }));
}

export async function undoAgentMemory(host: AgentMutationHost, messageId: string, memoryId: number): Promise<void> {
  host.patchAssistant(messageId, m => ({
    ...m,
    memories: m.memories.map(mem => (mem.id === memoryId ? { ...mem, undoing: true } : mem)),
  }));
  try {
    await postWithIdempotency(host, 'agent/undo-memory', { id: memoryId });
    host.patchAssistant(messageId, m => ({
      ...m,
      memories: m.memories.map(mem => (mem.id === memoryId ? { ...mem, undoing: false, undone: true } : mem)),
    }));
    GridService.reloadServices(['app/agent-memory']);
  } catch (err) {
    host.patchAssistant(messageId, m => ({
      ...m,
      memories: m.memories.map(mem => (mem.id === memoryId ? { ...mem, undoing: false } : mem)),
    }));
    host.alert.error(extractAgentErrorMessage(err, host.texts.of('RequestFailed')));
  }
}
