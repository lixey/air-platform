import { inject, Service, signal } from '@angular/core';
import { AlertService } from '../app/alert/alert.service';
import { LocaleService } from '../app/locale.service';
import { GridService } from '../grid/grid.service';
import { OverlayService } from '../overlay/overlay.service';

export interface AgentAttachment {
  readonly id: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly size: number;
  readonly previewUrl: string;
}

export interface AgentProposalField {
  readonly label: string;
  readonly value: string;
  readonly badge?: string | null;
  readonly color?: string | null;
}

export interface AgentProposalLine {
  readonly name: string;
  readonly qty: number;
  readonly price: number;
  readonly amount: number;
  readonly category?: string | null;
}

export interface AgentProposal {
  readonly id: string;
  readonly sessionId: string;
  readonly kind: string;
  readonly title: string;
  readonly subtitle?: string | null;
  readonly badge?: string | null;
  readonly badgeColor?: string | null;
  readonly fields: readonly AgentProposalField[];
  readonly lines: readonly AgentProposalLine[];
  readonly attachmentIds: readonly string[];
  status: 'pending' | 'confirming' | 'committed';
  committedRecordId?: number | null;
  committedLabel?: string | null;
  formService?: string | null;
}

export interface AgentChatMessage {
  readonly id: string;
  readonly role: 'user' | 'assistant';
  text: string;
  statusText?: string | null;
  readonly attachments: readonly AgentAttachment[];
  readonly proposals: AgentProposal[];
  readonly createdAt: Date;
}

interface AgentStreamEventDto {
  readonly type: 'status' | 'delta' | 'proposal' | 'done' | 'error';
  readonly text?: string | null;
  readonly proposal?: AgentProposal | null;
  readonly sessionId?: string | null;
}

interface AgentCommitResultDto {
  readonly proposalId: string;
  readonly recordId: number;
  readonly recordLabel: string;
  readonly formService: string;
  readonly refreshServices?: readonly string[];
}

function stripProposalBlocks(raw: string): string {
  return raw
    .replace(/<air_proposal>[\s\S]*?(?:<\/air_proposal>|$)/gi, '')
    .replace(/```(?:json|xml)?\s*<air_proposal>[\s\S]*$/gi, '')
    .trim();
}

@Service()
export class AgentService {
  private readonly overlay = inject(OverlayService);
  private readonly alert = inject(AlertService);
  private readonly locale = inject(LocaleService);

  private abortController: AbortController | null = null;
  private nextId = 0;

  readonly sessionId = signal<string>(Math.random().toString(36).slice(2, 12));
  readonly model = signal<string>('gemini-3.8-flash-low');
  readonly messages = signal<readonly AgentChatMessage[]>([]);
  readonly pendingAttachments = signal<readonly AgentAttachment[]>([]);
  readonly isUploading = signal(false);
  readonly isStreaming = signal(false);

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
        const res = await fetch('/api/agent/upload', {
          method: 'POST',
          body: form,
          credentials: 'include',
        });
        if (!res.ok) {
          throw new Error('Не вдалося завантажити файл.');
        }
        const saved = (await res.json()) as AgentAttachment;
        this.pendingAttachments.update(prev => [...prev, saved]);
      }
    } catch (err) {
      this.alert.error(err instanceof Error ? err.message : 'Помилка завантаження файлу');
    } finally {
      this.isUploading.set(false);
    }
  }

  removePendingAttachment(id: string): void {
    this.pendingAttachments.update(prev => prev.filter(a => a.id !== id));
  }

  async send(rawText: string): Promise<void> {
    const text = rawText.trim();
    const attachments = this.pendingAttachments();
    if ((!text && attachments.length === 0) || this.isStreaming()) {
      return;
    }

    this.pendingAttachments.set([]);

    const userMsg: AgentChatMessage = {
      id: `m-${++this.nextId}`,
      role: 'user',
      text: text || (attachments.length > 0 ? 'Розбери фото чека' : ''),
      attachments,
      proposals: [],
      createdAt: new Date(),
    };

    const assistantMsgId = `m-${++this.nextId}`;
    const assistantMsg: AgentChatMessage = {
      id: assistantMsgId,
      role: 'assistant',
      text: '',
      statusText: attachments.length > 0 ? 'Читаю фото чека…' : 'Аналізую…',
      attachments: [],
      proposals: [],
      createdAt: new Date(),
    };

    this.messages.update(prev => [...prev, userMsg, assistantMsg]);
    this.isStreaming.set(true);

    const controller = new AbortController();
    this.abortController = controller;

    let rawAccumulated = '';

    try {
      const response = await fetch('/api/agent/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          sessionId: this.sessionId(),
          message: text,
          attachmentIds: attachments.map(a => a.id),
          locale: this.locale.current(),
          model: this.model(),
        }),
        signal: controller.signal,
      });

      if (!response.ok || !response.body) {
        throw new Error(`Помилка сервера (${response.status})`);
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
                }));
              } else if (evt.type === 'delta' && evt.text) {
                rawAccumulated += evt.text;
                const cleanText = stripProposalBlocks(rawAccumulated);
                this.patchAssistant(assistantMsgId, m => ({
                  ...m,
                  text: cleanText,
                  statusText: null,
                }));
              } else if (evt.type === 'proposal' && evt.proposal) {
                const incoming = { ...evt.proposal, status: 'pending' as const };
                this.patchAssistant(assistantMsgId, m => {
                  const exists = m.proposals.some(p => p.id === incoming.id);
                  return {
                    ...m,
                    statusText: null,
                    proposals: exists ? m.proposals : [...m.proposals, incoming],
                  };
                });
              } else if (evt.type === 'error') {
                this.patchAssistant(assistantMsgId, m => ({
                  ...m,
                  text: m.text || (evt.text ?? 'Помилка виконання запиту.'),
                  statusText: null,
                }));
              } else if (evt.type === 'done') {
                this.patchAssistant(assistantMsgId, m => ({
                  ...m,
                  text: stripProposalBlocks(rawAccumulated) || m.text,
                  statusText: null,
                }));
              }
            } catch {
              // Ignore malformed SSE frame
            }
          }
        }
      }
    } catch (err) {
      if ((err as Error)?.name !== 'AbortError') {
        this.patchAssistant(assistantMsgId, m => ({
          ...m,
          text: m.text || 'Не вдалося отримати відповідь від AI агента.',
          statusText: null,
        }));
      }
    } finally {
      this.patchAssistant(assistantMsgId, m => ({
        ...m,
        statusText: null,
      }));
      this.isStreaming.set(false);
      this.abortController = null;
    }
  }

  stop(): void {
    this.abortController?.abort();
    this.abortController = null;
    this.isStreaming.set(false);
  }

  async confirmProposal(messageId: string, proposalId: string): Promise<void> {
    this.updateProposal(messageId, proposalId, p => ({ ...p, status: 'confirming' }));

    try {
      const res = await fetch(`/api/agent/proposals/${encodeURIComponent(proposalId)}/confirm`, {
        method: 'POST',
        credentials: 'include',
      });

      if (!res.ok) {
        const errBody = (await res.json().catch(() => null)) as { title?: string } | null;
        throw new Error(errBody?.title || 'Не вдалося зберегти операцію.');
      }

      const committed = (await res.json()) as AgentCommitResultDto;
      this.updateProposal(messageId, proposalId, p => ({
        ...p,
        status: 'committed',
        committedRecordId: committed.recordId,
        committedLabel: committed.recordLabel,
        formService: committed.formService,
      }));

      GridService.reloadServices(committed.refreshServices);
    } catch (err) {
      this.updateProposal(messageId, proposalId, p => ({ ...p, status: 'pending' }));
      this.alert.error(err instanceof Error ? err.message : 'Помилка збереження');
    }
  }

  openRecord(formService: string | null | undefined, recordId: number | null | undefined): void {
    if (!formService || !recordId) {
      return;
    }

    const ref = this.overlay.open({
      key: formService,
      presentation: 'drawer',
      closesOnEscape: true,
      screen: {
        service: formService,
        params: { id: recordId },
      },
    });

    ref.closed.subscribe(() => GridService.reloadServices());
  }

  async reset(): Promise<void> {
    this.stop();
    const oldSessionId = this.sessionId();
    this.messages.set([]);
    this.pendingAttachments.set([]);
    this.sessionId.set(Math.random().toString(36).slice(2, 12));

    await fetch('/api/agent/reset', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ sessionId: oldSessionId }),
    }).catch(() => undefined);
  }

  private patchAssistant(messageId: string, updater: (msg: AgentChatMessage) => AgentChatMessage): void {
    this.messages.update(list => list.map(m => (m.id === messageId ? updater(m) : m)));
  }

  private updateProposal(
    messageId: string,
    proposalId: string,
    updater: (proposal: AgentProposal) => AgentProposal,
  ): void {
    this.patchAssistant(messageId, m => ({
      ...m,
      proposals: m.proposals.map(p => (p.id === proposalId ? updater(p) : p)),
    }));
  }
}
