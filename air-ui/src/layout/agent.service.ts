import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Service, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AGENT_BLOCK_PLUGINS, type AgentContentBlock } from '../agent/agent-block-models';
import { parseAgentBlocks } from '../agent/agent-block-parser';
import { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { PLATFORM_CLIENT_HEADER, PLATFORM_TIME_ZONE_HEADER } from '../app/auth/credentials.interceptor';
import { LocaleService } from '../app/locale.service';
import type {
  AgentAttachmentDto,
  AgentCommitResultDto,
  AgentModelDto,
  AgentProposalDto,
  AgentProposalFieldDto,
  AgentProposalLineDto,
  AgentStatusDto,
  AgentStreamEventDto,
} from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { browserZone } from '../core/time-zone';
import { GridService } from '../grid/grid.service';
import { OverlayService } from '../overlay/overlay.service';

export type AgentAttachment = AgentAttachmentDto;
export type AgentProposalField = AgentProposalFieldDto;
export type AgentProposalLine = AgentProposalLineDto;
export type AgentProposal = AgentProposalDto;
export type AgentModel = AgentModelDto;

export interface AgentChatMessage {
  readonly id: string;
  readonly role: 'user' | 'assistant';
  text: string;
  readonly blocks?: readonly AgentContentBlock[];
  statusText?: string | null;
  readonly attachments: readonly AgentAttachment[];
  readonly proposals: AgentProposal[];
  readonly createdAt: Date;
  readonly durationMs?: number;
  readonly modelId?: string;
  readonly modelName?: string;
  readonly promptText?: string;
  readonly promptAttachments?: readonly AgentAttachment[];
}

function stripProposalBlocks(raw: string): string {
  return raw
    .replace(/<air_proposal>[\s\S]*?(?:<\/air_proposal>|$)/gi, '')
    .replace(/```(?:json|xml)?\s*<air_proposal>[\s\S]*$/gi, '')
    .replace(/<air_memory>[\s\S]*?(?:<\/air_memory>|$)/gi, '')
    .replace(/```(?:json|xml)?\s*<air_memory>[\s\S]*$/gi, '')
    .trim();
}

@Service()
export class AgentService {
  private readonly http = inject(HttpClient);
  private readonly overlay = inject(OverlayService);
  private readonly alert = inject(AlertService);
  private readonly locale = inject(LocaleService);
  private readonly texts = inject(TextsService);
  private readonly blockPlugins = inject(AGENT_BLOCK_PLUGINS);

  private abortController: AbortController | null = null;
  private nextId = 0;
  private statusLoaded = false;

  readonly sessionId = signal<string>(Math.random().toString(36).slice(2, 12));
  readonly models = signal<readonly AgentModelDto[]>([]);
  readonly model = signal<string>('');
  readonly activeProvider = signal<string>('Agy');
  readonly userPickedModel = signal(false);
  readonly messages = signal<readonly AgentChatMessage[]>([]);
  readonly pendingAttachments = signal<readonly AgentAttachment[]>([]);
  readonly isUploading = signal(false);
  readonly isStreaming = signal(false);

  async ensureStatusLoaded(force = false): Promise<void> {
    if (this.statusLoaded && !force) {
      return;
    }
    this.statusLoaded = true;
    try {
      const status = await firstValueFrom(this.http.get<AgentStatusDto>('/api/agent/status', shownByCaller()));
      if (status.models?.length) {
        this.models.set(status.models);
      }
      const curModel = this.model();
      const curProvider = this.activeProvider();
      const hasCurrentPair =
        !!curModel && !!status.models?.some(m => m.id === curModel && (!curProvider || !m.provider || m.provider === curProvider));
      if (!hasCurrentPair) {
        const defEntry =
          status.models?.find(m => m.isDefault) ??
          (status.defaultModel ? status.models?.find(m => m.id === status.defaultModel) : undefined) ??
          status.models?.[0];
        const nextModel = defEntry?.id || status.defaultModel || '';
        this.model.set(nextModel);
        if (defEntry?.provider) {
          this.activeProvider.set(defEntry.provider);
        } else if (status.activeProvider) {
          this.activeProvider.set(status.activeProvider);
        }
      }
    } catch {
      this.statusLoaded = false;
    }
  }

  async uploadFiles(files: FileList | readonly File[]): Promise<void> {
    const list = Array.from(files);
    if (list.length === 0) {
      return;
    }

    this.isUploading.set(true);
    try {
      for (const file of list) {
        const form = new FormData();
        form.append('file', file, file.name);
        const saved = await firstValueFrom(this.http.post<AgentAttachment>('/api/agent/upload', form, shownByCaller()));
        this.pendingAttachments.update(prev => [...prev, saved]);
      }
    } catch (err) {
      const msg =
        err instanceof HttpErrorResponse && err.error?.title
          ? String(err.error.title)
          : err instanceof Error
            ? err.message
            : this.texts.of('AgentErrorUpload');
      this.alert.error(msg);
    } finally {
      this.isUploading.set(false);
    }
  }

  removePendingAttachment(id: string): void {
    this.pendingAttachments.update(prev => prev.filter(a => a.id !== id));
  }

  async send(rawText: string, explicitAttachments?: readonly AgentAttachment[]): Promise<void> {
    const text = rawText.trim();
    const attachments = explicitAttachments ?? this.pendingAttachments();
    if ((!text && attachments.length === 0) || this.isStreaming()) {
      return;
    }

    if (!explicitAttachments) {
      this.pendingAttachments.set([]);
    }

    const effectivePrompt = text || (attachments.length > 0 ? this.texts.of('AgentDefaultPhotoPrompt') : '');
    const userMsg: AgentChatMessage = {
      id: `m-${++this.nextId}`,
      role: 'user',
      text: effectivePrompt,
      attachments,
      proposals: [],
      createdAt: new Date(),
      promptText: effectivePrompt,
      promptAttachments: attachments,
    };

    const assistantMsgId = `m-${++this.nextId}`;
    const startedAt = performance.now();
    const modelsList = this.models();
    const activeModelObj =
      attachments.length > 0 && !this.userPickedModel()
        ? (modelsList.find(m => m.isForPhotos) ??
          modelsList.find(m => m.id === this.model() && m.provider === this.activeProvider()) ??
          modelsList.find(m => m.id === this.model()))
        : (modelsList.find(m => m.id === this.model() && m.provider === this.activeProvider()) ??
          modelsList.find(m => m.id === this.model()));
    const assistantMsg: AgentChatMessage = {
      id: assistantMsgId,
      role: 'assistant',
      text: '',
      statusText: attachments.length > 0 ? this.texts.of('AgentStatusReadingPhoto') : this.texts.of('AgentStatusAnalyzing'),
      attachments: [],
      proposals: [],
      createdAt: new Date(),
      durationMs: 0,
      modelId: activeModelObj?.id || this.model(),
      modelName: activeModelObj?.name || this.model(),
      promptText: effectivePrompt,
      promptAttachments: attachments,
    };

    this.messages.update(prev => [...prev, userMsg, assistantMsg]);
    this.isStreaming.set(true);

    const controller = new AbortController();
    this.abortController = controller;
    const durationTimer = setInterval(() => {
      this.patchAssistant(assistantMsgId, m => ({
        ...m,
        durationMs: Math.max(0, Math.round(performance.now() - startedAt)),
      }));
    }, 200);

    let rawAccumulated = '';

    try {
      const response = await fetch('/api/agent/chat', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          [PLATFORM_CLIENT_HEADER]: 'browser',
          [PLATFORM_TIME_ZONE_HEADER]: browserZone(),
        },
        credentials: 'include',
        body: JSON.stringify({
          sessionId: this.sessionId(),
          message: effectivePrompt,
          attachmentIds: attachments.map(a => a.id),
          locale: this.locale.current(),
          provider: activeModelObj?.provider || this.activeProvider(),
          model: activeModelObj?.id || this.model(),
        }),
        signal: controller.signal,
      });

      if (!response.ok || !response.body) {
        throw new Error(`${this.texts.of('RequestFailed')} (${response.status})`);
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) {
          break;
        }

        buffer += decoder.decode(value, { stream: true });
        const parts = buffer.split('\n\n');
        buffer = parts.pop() ?? '';

        for (const part of parts) {
          for (const line of part.split('\n')) {
            if (!line.startsWith('data:')) {
              continue;
            }
            const jsonStr = line.slice(5).trim();
            if (!jsonStr) {
              continue;
            }

            try {
              const evt = JSON.parse(jsonStr) as AgentStreamEventDto;
              if (evt.type === 'status') {
                this.patchAssistant(assistantMsgId, m => ({
                  ...m,
                  statusText: evt.text ?? null,
                  modelId: evt.modelId ?? m.modelId,
                  modelName: evt.modelName ?? m.modelName,
                }));
              } else if (evt.type === 'delta' && evt.text) {
                rawAccumulated += evt.text;
                const cleanText = stripProposalBlocks(rawAccumulated);
                const blocks = parseAgentBlocks(cleanText, this.blockPlugins, assistantMsgId);
                this.patchAssistant(assistantMsgId, m => ({
                  ...m,
                  text: cleanText,
                  blocks,
                  statusText: null,
                }));
              } else if (evt.type === 'proposal' && evt.proposal) {
                const incoming: AgentProposal = { ...evt.proposal, status: 'pending' };
                this.patchAssistant(assistantMsgId, m => {
                  const exists = m.proposals.some(p => p.id === incoming.id);
                  return {
                    ...m,
                    statusText: null,
                    proposals: exists ? m.proposals : [...m.proposals, incoming],
                  };
                });
              } else if (evt.type === 'error') {
                this.patchAssistant(assistantMsgId, m => {
                  const nextText = m.text || (evt.text ?? this.texts.of('RequestFailed'));
                  return {
                    ...m,
                    text: nextText,
                    blocks: parseAgentBlocks(nextText, this.blockPlugins, assistantMsgId),
                    statusText: null,
                  };
                });
              } else if (evt.type === 'done') {
                this.patchAssistant(assistantMsgId, m => {
                  const nextText = stripProposalBlocks(rawAccumulated) || m.text;
                  return {
                    ...m,
                    text: nextText,
                    blocks: parseAgentBlocks(nextText, this.blockPlugins, assistantMsgId),
                    statusText: null,
                    modelId: evt.modelId ?? m.modelId,
                    modelName: evt.modelName ?? m.modelName,
                  };
                });
              }
            } catch {
              // Ignore malformed SSE frame
            }
          }
        }
      }
    } catch (err) {
      if ((err as Error)?.name !== 'AbortError') {
        this.patchAssistant(assistantMsgId, m => {
          const nextText = m.text || this.texts.of('AgentErrorRequest');
          return {
            ...m,
            text: nextText,
            blocks: parseAgentBlocks(nextText, this.blockPlugins, assistantMsgId),
            statusText: null,
          };
        });
      }
    } finally {
      clearInterval(durationTimer);
      const finalDuration = Math.max(0, Math.round(performance.now() - startedAt));
      this.patchAssistant(assistantMsgId, m => ({
        ...m,
        statusText: null,
        durationMs: finalDuration,
        createdAt: new Date(),
      }));
      this.isStreaming.set(false);
      this.abortController = null;
    }
  }

  async retryMessage(messageId: string): Promise<void> {
    if (this.isStreaming()) {
      return;
    }

    const list = this.messages();
    const idx = list.findIndex(m => m.id === messageId);
    if (idx < 0) {
      return;
    }

    const target = list[idx];
    if (target.role === 'assistant') {
      const prevUser = idx > 0 && list[idx - 1]?.role === 'user' ? list[idx - 1] : null;
      const promptText = target.promptText ?? prevUser?.text ?? '';
      const promptAttachments = target.promptAttachments ?? prevUser?.attachments ?? [];
      if (!promptText && promptAttachments.length === 0) {
        return;
      }

      const removeIds = new Set<string>([target.id]);
      if (prevUser) {
        removeIds.add(prevUser.id);
      }
      this.messages.update(prev => prev.filter(m => !removeIds.has(m.id)));
      await this.send(promptText, promptAttachments);
      return;
    }

    const promptText = target.text;
    const promptAttachments = target.attachments;
    if (!promptText && promptAttachments.length === 0) {
      return;
    }

    const nextAssistant = idx + 1 < list.length && list[idx + 1]?.role === 'assistant' ? list[idx + 1] : null;
    if (nextAssistant && idx + 1 === list.length - 1) {
      const removeIds = new Set<string>([target.id, nextAssistant.id]);
      this.messages.update(prev => prev.filter(m => !removeIds.has(m.id)));
    }

    await this.send(promptText, promptAttachments);
  }

  stop(): void {
    this.abortController?.abort();
    this.abortController = null;
    this.isStreaming.set(false);
  }

  async confirmProposal(messageId: string, proposalId: string): Promise<void> {
    this.updateProposal(messageId, proposalId, p => ({ ...p, status: 'confirming' }));

    try {
      const committed = await firstValueFrom(
        this.http.post<AgentCommitResultDto>(`/api/agent/proposals/${encodeURIComponent(proposalId)}/confirm`, {}, shownByCaller()),
      );
      this.applyCommitResult(committed);

      if (committed.alsoCommitted) {
        for (const extra of committed.alsoCommitted) {
          this.applyCommitResult(extra);
        }
      }

      GridService.reloadServices(committed.refreshServices);
    } catch (err) {
      this.updateProposal(messageId, proposalId, p => ({ ...p, status: 'pending' }));
      const msg =
        err instanceof HttpErrorResponse && err.error?.title
          ? String(err.error.title)
          : err instanceof Error
            ? err.message
            : this.texts.of('AgentErrorSave');
      this.alert.error(msg);
    }
  }

  openRecord(proposal: AgentProposal, explicitId?: number, explicitService?: string): void {
    const formService = explicitService ?? proposal.formService ?? proposal.service;
    const recordId = explicitId ?? proposal.committedRecordId ?? proposal.recordId;
    if (!formService || !recordId) {
      return;
    }

    const baseOverlay = proposal.overlay ?? {
      key: formService,
      presentation: 'popover' as const,
      width: '720px',
      closesOnEscape: true,
    };

    const ref = this.overlay.open({
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

  async reset(): Promise<void> {
    this.stop();
    const oldSessionId = this.sessionId();
    this.userPickedModel.set(false);
    this.messages.set([]);
    this.pendingAttachments.set([]);
    this.sessionId.set(Math.random().toString(36).slice(2, 12));

    await firstValueFrom(this.http.post('/api/agent/reset', { sessionId: oldSessionId }, shownByCaller())).catch(() => undefined);
  }

  private applyCommitResult(result: AgentCommitResultDto): void {
    this.messages.update(list =>
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

  private patchAssistant(messageId: string, updater: (msg: AgentChatMessage) => AgentChatMessage): void {
    this.messages.update(list => list.map(m => (m.id === messageId ? updater(m) : m)));
  }

  private updateProposal(messageId: string, proposalId: string, updater: (proposal: AgentProposal) => AgentProposal): void {
    this.patchAssistant(messageId, m => ({
      ...m,
      proposals: m.proposals.map(p => (p.id === proposalId ? updater(p) : p)),
    }));
  }
}
