import { firstValueFrom } from 'rxjs';
import type { AuthService } from '../app/auth/auth.service';
import { PLATFORM_CLIENT_HEADER, PLATFORM_TIME_ZONE_HEADER } from '../app/auth/credentials.interceptor';
import type { AgentStreamEventDto } from '../core/generated/contracts';
import type { TextsService } from '../core/texts.service';
import { browserZone } from '../core/time-zone';
import { GridService } from '../grid/grid.service';
import type { AgentBlockPlugin } from './agent-block-models';
import { parseAgentBlocks, stripAgentControlBlocks } from './agent-block-parser';
import type { AgentChatMessage, AgentProposal } from './agent-models';

const STREAM_DELTA_COALESCE_MS = 32;
const STREAM_DURATION_TICK_MS = 200;

export interface AgentStreamContext {
  readonly endpoint: string;
  readonly assistantMsgId: string;
  readonly requestBody: unknown;
  readonly controller: AbortController;
  readonly auth: AuthService;
  readonly currentPath: () => string | null;
  readonly texts: TextsService;
  readonly blockPlugins: readonly AgentBlockPlugin[];
  readonly onDurationTick: (durationMs: number) => void;
  readonly patchAssistant: (messageId: string, updater: (msg: AgentChatMessage) => AgentChatMessage) => void;
  readonly supersedeProposals: (supersededIds: readonly string[]) => void;
  readonly reloadStatus: () => void;
}

/**
 * Executes an SSE request (`/api/agent/chat` or `/api/agent/sync`), handling 401 refresh retry, coalesced delta
 * flushes, proposal/memory/supersede events, and live duration ticks without full-message-array polling clones.
 */
export async function runAgentSseStream(ctx: AgentStreamContext): Promise<number> {
  const startedAt = performance.now();
  const durationTimer = setInterval(() => {
    ctx.onDurationTick(Math.max(0, Math.round(performance.now() - startedAt)));
  }, STREAM_DURATION_TICK_MS);

  let rawAccumulated = '';
  let hasUnflushedDelta = false;
  let hasFlushedFirstDelta = false;
  let deltaTimer: ReturnType<typeof setTimeout> | null = null;

  const flushDeltaNow = (): void => {
    if (deltaTimer !== null) {
      clearTimeout(deltaTimer);
      deltaTimer = null;
    }
    if (!hasUnflushedDelta) {
      return;
    }
    hasUnflushedDelta = false;
    hasFlushedFirstDelta = true;
    const cleanText = stripAgentControlBlocks(rawAccumulated);
    const blocks = parseAgentBlocks(cleanText, ctx.blockPlugins, ctx.assistantMsgId);
    ctx.patchAssistant(ctx.assistantMsgId, m => ({
      ...m,
      text: cleanText,
      blocks,
      statusText: null,
    }));
  };

  const scheduleDeltaFlush = (): void => {
    hasUnflushedDelta = true;
    if (!hasFlushedFirstDelta) {
      flushDeltaNow();
      return;
    }
    if (deltaTimer === null) {
      deltaTimer = setTimeout(flushDeltaNow, STREAM_DELTA_COALESCE_MS);
    }
  };

  const handleEvent = (evt: AgentStreamEventDto): void => {
    if (evt.type === 'status') {
      ctx.patchAssistant(ctx.assistantMsgId, m => ({
        ...m,
        statusText: evt.text ?? null,
        modelId: evt.modelId ?? m.modelId,
        modelName: evt.modelName ?? m.modelName,
      }));
    } else if (evt.type === 'delta' && evt.text) {
      rawAccumulated += evt.text;
      scheduleDeltaFlush();
    } else if (evt.type === 'supersede' && evt.supersededProposalIds?.length) {
      flushDeltaNow();
      ctx.supersedeProposals(evt.supersededProposalIds);
    } else if (evt.type === 'refresh') {
      GridService.reloadServices(evt.refreshServices ?? undefined);
      ctx.reloadStatus();
    } else if (evt.type === 'proposal' && evt.proposal) {
      flushDeltaNow();
      const incoming: AgentProposal = { ...evt.proposal, status: 'pending' };
      ctx.patchAssistant(ctx.assistantMsgId, m => ({
        ...m,
        statusText: null,
        proposals: m.proposals.some(p => p.id === incoming.id) ? m.proposals : [...m.proposals, incoming],
      }));
    } else if (evt.type === 'memory' && evt.memory) {
      flushDeltaNow();
      const incomingMem = evt.memory;
      ctx.patchAssistant(ctx.assistantMsgId, m => ({
        ...m,
        memories: m.memories.some(mem =>
          incomingMem.pendingToken ? mem.pendingToken === incomingMem.pendingToken : mem.id > 0 && mem.id === incomingMem.id,
        )
          ? m.memories
          : [...m.memories, { ...incomingMem }],
      }));
    } else if (evt.type === 'error') {
      flushDeltaNow();
      ctx.patchAssistant(ctx.assistantMsgId, m => {
        const nextText = m.text || (evt.text ?? ctx.texts.of('RequestFailed'));
        return {
          ...m,
          text: nextText,
          blocks: parseAgentBlocks(nextText, ctx.blockPlugins, ctx.assistantMsgId),
          statusText: null,
        };
      });
    } else if (evt.type === 'done') {
      flushDeltaNow();
      ctx.patchAssistant(ctx.assistantMsgId, m => {
        const nextText = stripAgentControlBlocks(rawAccumulated) || m.text;
        return {
          ...m,
          text: nextText,
          blocks: parseAgentBlocks(nextText, ctx.blockPlugins, ctx.assistantMsgId),
          statusText: null,
          modelId: evt.modelId ?? m.modelId,
          modelName: evt.modelName ?? m.modelName,
        };
      });
    }
  };

  try {
    const doFetch = () =>
      fetch(ctx.endpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          [PLATFORM_CLIENT_HEADER]: 'browser',
          [PLATFORM_TIME_ZONE_HEADER]: browserZone(),
        },
        credentials: 'include',
        body: JSON.stringify(ctx.requestBody),
        signal: ctx.controller.signal,
      });

    let response = await doFetch();
    if (response.status === 401 && ctx.auth.isLoggedIn()) {
      const refreshed = await firstValueFrom(ctx.auth.refresh()).catch(() => null);
      if (refreshed === true) {
        response = await doFetch();
        if (response.status === 401 && ctx.auth.isLoggedIn()) {
          ctx.auth.sessionLost(ctx.currentPath());
        }
      } else if (refreshed === false && ctx.auth.isLoggedIn()) {
        ctx.auth.sessionLost(ctx.currentPath());
      }
    }

    if (!response.ok || !response.body) {
      throw new Error(`${ctx.texts.of('RequestFailed')} (${response.status})`);
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    const processPart = (part: string): void => {
      for (const line of part.split('\n')) {
        if (!line.startsWith('data:')) {
          continue;
        }
        const jsonStr = line.slice(5).trim();
        if (!jsonStr) {
          continue;
        }
        try {
          handleEvent(JSON.parse(jsonStr) as AgentStreamEventDto);
        } catch {
          // Ignore malformed SSE frame
        }
      }
    };

    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        buffer += decoder.decode();
        for (const part of buffer.split('\n\n')) {
          if (part.trim()) {
            processPart(part);
          }
        }
        break;
      }

      buffer += decoder.decode(value, { stream: true });
      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';
      for (const part of parts) {
        processPart(part);
      }
    }
  } catch (err) {
    flushDeltaNow();
    if ((err as Error)?.name !== 'AbortError') {
      ctx.patchAssistant(ctx.assistantMsgId, m => {
        const nextText = m.text || ctx.texts.of('AgentErrorRequest');
        return {
          ...m,
          text: nextText,
          blocks: parseAgentBlocks(nextText, ctx.blockPlugins, ctx.assistantMsgId),
          statusText: null,
        };
      });
    }
  } finally {
    flushDeltaNow();
    clearInterval(durationTimer);
  }

  return Math.max(0, Math.round(performance.now() - startedAt));
}
