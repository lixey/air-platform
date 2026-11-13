import { Location } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { effect, inject, Service, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { AuthService, identityOf } from '../app/auth/auth.service';
import { LocaleService } from '../app/locale.service';
import type { AgentModelDto, AgentStatusDto, AgentSyncSourceDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { OverlayService } from '../overlay/overlay.service';
import { AGENT_BLOCK_PLUGINS } from './agent-block-models';
import {
  type AgentAttachment,
  type AgentChatMessage,
  type AgentMemoryItem,
  type AgentModel,
  type AgentProposal,
  type AgentProposalChildColumn,
  type AgentProposalChildGroup,
  type AgentProposalChildRow,
  type AgentProposalField,
  createSessionId,
  resolveAgentRetryPlan,
  resolveAgentTurnModel,
} from './agent-models';
import { type AgentMutationHost, confirmAgentBatch, confirmAgentProposal, undoAgentMemory } from './agent-mutations';
import { runAgentSseStream } from './agent-stream';
import { openAgentProposalRecord } from './agent-ui-helpers';
import { uploadAgentFiles, uploadSingleAgentFile } from './agent-upload';

export type {
  AgentAttachment,
  AgentChatMessage,
  AgentMemoryItem,
  AgentModel,
  AgentProposal,
  AgentProposalChildColumn,
  AgentProposalChildGroup,
  AgentProposalChildRow,
  AgentProposalField,
};

@Service()
export class AgentService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly location = inject(Location);
  private readonly overlay = inject(OverlayService);
  private readonly alert = inject(AlertService);
  private readonly locale = inject(LocaleService);
  private readonly texts = inject(TextsService);
  private readonly blockPlugins = inject(AGENT_BLOCK_PLUGINS);

  private abortController: AbortController | null = null;
  private nextId = 0;
  private statusLoaded = false;
  private lastIdentity = identityOf(this.auth.currentUser());
  private readonly uncertainKeys = new Map<string, string>();

  readonly sessionId = signal<string>(createSessionId());
  readonly models = signal<readonly AgentModelDto[]>([]);
  readonly syncSources = signal<readonly AgentSyncSourceDto[]>([]);
  readonly model = signal<string>('');
  readonly activeProvider = signal<string>('Agy');
  readonly userPickedModel = signal(false);
  readonly messages = signal<readonly AgentChatMessage[]>([]);
  readonly pendingAttachments = signal<readonly AgentAttachment[]>([]);
  readonly pendingSyncSource = signal<AgentSyncSourceDto | null>(null);
  readonly pendingSyncFile = signal<AgentAttachment | null>(null);
  readonly isUploading = signal(false);
  readonly isStreaming = signal(false);
  readonly streamingMessageId = signal<string | null>(null);
  readonly streamingDurationMs = signal(0);

  private readonly mutationHost: AgentMutationHost = {
    http: this.http,
    alert: this.alert,
    texts: this.texts,
    uncertainKeys: this.uncertainKeys,
    getMessages: () => this.messages(),
    updateMessages: updater => this.messages.update(updater),
    patchAssistant: (id, updater) => this.patchAssistant(id, updater),
    updateProposal: (msgId, propId, updater) => this.updateProposal(msgId, propId, updater),
  };

  constructor() {
    effect(() => {
      const nextIdentity = identityOf(this.auth.currentUser());
      if (nextIdentity === this.lastIdentity) {
        return;
      }
      this.lastIdentity = nextIdentity;
      untracked(() => {
        this.stop();
        this.statusLoaded = false;
        this.uncertainKeys.clear();
        this.userPickedModel.set(false);
        this.models.set([]);
        this.syncSources.set([]);
        this.model.set('');
        this.activeProvider.set('Agy');
        this.messages.set([]);
        this.pendingAttachments.set([]);
        this.pendingSyncSource.set(null);
        this.pendingSyncFile.set(null);
        this.sessionId.set(createSessionId());
      });
    });
  }

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
      this.syncSources.set(status.syncSources ?? []);
      const curModel = this.model();
      const curProvider = this.activeProvider();
      const hasCurrentPair =
        !!curModel && !!status.models?.some(m => m.id === curModel && (!curProvider || !m.provider || m.provider === curProvider));
      if (!hasCurrentPair) {
        const defEntry =
          status.models?.find(m => m.isDefault) ??
          (status.defaultModel ? status.models?.find(m => m.id === status.defaultModel) : undefined) ??
          status.models?.[0];
        this.model.set(defEntry?.id || status.defaultModel || '');
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

  uploadFiles(files: FileList | readonly File[]): Promise<void> {
    return uploadAgentFiles(
      this.http,
      this.alert,
      this.texts,
      files,
      saved => this.pendingAttachments.update(prev => [...prev, saved]),
      v => this.isUploading.set(v),
    );
  }

  uploadSingleFile(file: File): Promise<AgentAttachment | null> {
    return uploadSingleAgentFile(this.http, this.alert, this.texts, file, v => this.isUploading.set(v));
  }

  removePendingAttachment(id: string): void {
    this.pendingAttachments.update(prev => prev.filter(a => a.id !== id));
  }

  setPendingSync(source: AgentSyncSourceDto, syncFile: AgentAttachment | null = null): void {
    this.pendingSyncSource.set(source);
    this.pendingSyncFile.set(syncFile);
  }

  clearPendingSync(): void {
    this.pendingSyncSource.set(null);
    this.pendingSyncFile.set(null);
  }

  toggleStepsCollapsed(messageId: string): void {
    this.patchAssistant(messageId, m => ({ ...m, stepsCollapsed: !m.stepsCollapsed }));
  }

  async runSync(
    source: AgentSyncSourceDto,
    rawText = '',
    explicitAttachments?: readonly AgentAttachment[],
    explicitSyncFile?: AgentAttachment | null,
  ): Promise<void> {
    if (this.isStreaming()) {
      return;
    }
    const text = rawText.trim();
    const attachments = explicitAttachments ?? this.pendingAttachments();
    const syncFile = explicitSyncFile !== undefined ? explicitSyncFile : this.pendingSyncFile();
    if (!explicitAttachments) {
      this.pendingAttachments.set([]);
    }
    if (explicitSyncFile === undefined) {
      this.pendingSyncSource.set(null);
      this.pendingSyncFile.set(null);
    }

    const syncLabel = this.texts.of('AgentSyncPromptLabel', source.label);
    const userMsg: AgentChatMessage = {
      id: `m-${++this.nextId}`,
      role: 'user',
      text: text ? `${syncLabel}\n\n${text}` : syncLabel,
      attachments: syncFile ? [syncFile, ...attachments] : attachments,
      proposals: [],
      memories: [],
      createdAt: new Date(),
      promptText: text,
      promptAttachments: attachments,
      syncSource: source,
    };

    const assistantMsgId = `m-${++this.nextId}`;
    const { activeModelObj } = resolveAgentTurnModel(
      this.models(),
      this.model(),
      this.activeProvider(),
      this.userPickedModel(),
      attachments,
    );
    const assistantMsg: AgentChatMessage = {
      id: assistantMsgId,
      role: 'assistant',
      text: '',
      statusText: this.texts.of('AgentStatusSyncing'),
      attachments: [],
      proposals: [],
      memories: [],
      createdAt: new Date(),
      durationMs: 0,
      modelId: activeModelObj?.id || this.model(),
      modelName: activeModelObj?.name || this.model(),
      promptText: text,
      promptAttachments: attachments,
      syncSource: source,
    };

    this.messages.update(prev => [...prev, userMsg, assistantMsg]);
    await this.streamSseEndpoint('/api/agent/sync', assistantMsgId, {
      sessionId: this.sessionId(),
      sourceId: source.id,
      message: text || null,
      syncFileAttachmentId: syncFile?.id ?? null,
      attachmentIds: attachments.map(a => a.id),
      locale: this.locale.current(),
      provider: activeModelObj?.provider || this.activeProvider(),
      model: activeModelObj?.id || this.model(),
    });
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
      memories: [],
      createdAt: new Date(),
      promptText: effectivePrompt,
      promptAttachments: attachments,
    };

    const assistantMsgId = `m-${++this.nextId}`;
    const { activeModelObj, hasImageAttachment } = resolveAgentTurnModel(
      this.models(),
      this.model(),
      this.activeProvider(),
      this.userPickedModel(),
      attachments,
    );
    const assistantMsg: AgentChatMessage = {
      id: assistantMsgId,
      role: 'assistant',
      text: '',
      statusText: hasImageAttachment ? this.texts.of('AgentStatusReadingPhoto') : this.texts.of('AgentStatusAnalyzing'),
      attachments: [],
      proposals: [],
      memories: [],
      createdAt: new Date(),
      durationMs: 0,
      modelId: activeModelObj?.id || this.model(),
      modelName: activeModelObj?.name || this.model(),
      promptText: effectivePrompt,
      promptAttachments: attachments,
    };

    this.messages.update(prev => [...prev, userMsg, assistantMsg]);
    await this.streamSseEndpoint('/api/agent/chat', assistantMsgId, {
      sessionId: this.sessionId(),
      message: effectivePrompt,
      attachmentIds: attachments.map(a => a.id),
      locale: this.locale.current(),
      provider: activeModelObj?.provider || this.activeProvider(),
      model: activeModelObj?.id || this.model(),
    });
  }

  private async streamSseEndpoint(endpoint: string, assistantMsgId: string, requestBody: unknown): Promise<void> {
    const controller = new AbortController();
    this.abortController = controller;
    this.isStreaming.set(true);
    this.streamingMessageId.set(assistantMsgId);
    this.streamingDurationMs.set(0);

    try {
      const finalDuration = await runAgentSseStream({
        endpoint,
        assistantMsgId,
        requestBody,
        controller,
        auth: this.auth,
        currentPath: () => this.location.path() || null,
        texts: this.texts,
        blockPlugins: this.blockPlugins,
        onDurationTick: ms => this.streamingDurationMs.set(ms),
        patchAssistant: (id, updater) => this.patchAssistant(id, updater),
        supersedeProposals: supersededIds => {
          const supSet = new Set(supersededIds);
          this.messages.update(list =>
            list.map(m => ({
              ...m,
              proposals: m.proposals.map(p => (supSet.has(p.id) && p.status === 'pending' ? { ...p, status: 'superseded' } : p)),
            })),
          );
        },
        reloadStatus: () => void this.ensureStatusLoaded(true),
      });

      this.patchAssistant(assistantMsgId, m => ({
        ...m,
        statusText: null,
        durationMs: finalDuration,
        createdAt: new Date(),
      }));
    } finally {
      if (this.abortController === controller) {
        this.abortController = null;
        this.isStreaming.set(false);
        this.streamingMessageId.set(null);
        this.streamingDurationMs.set(0);
      }
    }
  }

  async retryMessage(messageId: string): Promise<void> {
    if (this.isStreaming()) {
      return;
    }
    const plan = resolveAgentRetryPlan(this.messages(), messageId);
    if (!plan) {
      return;
    }
    if (plan.removeIds.size > 0) {
      this.messages.update(prev => prev.filter(m => !plan.removeIds.has(m.id)));
    }
    await this.send(plan.promptText, plan.promptAttachments);
  }

  stop(): void {
    this.abortController?.abort();
    this.abortController = null;
    this.isStreaming.set(false);
    this.streamingMessageId.set(null);
    this.streamingDurationMs.set(0);
  }

  confirmProposal(messageId: string, proposalId: string): Promise<void> {
    return confirmAgentProposal(this.mutationHost, messageId, proposalId);
  }

  confirmBatch(messageId: string): Promise<void> {
    return confirmAgentBatch(this.mutationHost, messageId);
  }

  undoMemory(messageId: string, memoryId: number): Promise<void> {
    return undoAgentMemory(this.mutationHost, messageId, memoryId);
  }

  openRecord(proposal: AgentProposal, explicitId?: number, explicitService?: string): void {
    openAgentProposalRecord(this.overlay, proposal, explicitId, explicitService);
  }

  async reset(): Promise<void> {
    this.stop();
    const oldSessionId = this.sessionId();
    this.uncertainKeys.clear();
    this.userPickedModel.set(false);
    this.messages.set([]);
    this.pendingAttachments.set([]);
    this.pendingSyncSource.set(null);
    this.pendingSyncFile.set(null);
    this.sessionId.set(createSessionId());
    await firstValueFrom(this.http.post('/api/agent/reset', { sessionId: oldSessionId }, shownByCaller())).catch(() => undefined);
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
