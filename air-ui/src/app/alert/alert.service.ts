import { Service, signal } from '@angular/core';
import type { MessageDto } from '../../core/generated/contracts';

/** A message on screen: what the server or a caller said, under an id the service gave it. */
export interface AlertMessage extends MessageDto {
  id: string;
}

/** What a caller opens: a message, with or without an id of its own. */
export type AlertInput = MessageDto & { id?: string | null };

const AUTO_CLOSE_MS = 4000;

@Service()
export class AlertService {
  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly _messages = signal<AlertMessage[]>([]);
  private nextId = 0;

  readonly messages = this._messages.asReadonly();

  /** Shows one message or several beside those already showing. An error stays until closed. */
  open(messages: AlertInput[] | AlertInput): void {
    const rawItems = (Array.isArray(messages) ? messages : [messages]).map(msg => ({
      ...msg,
      id: msg.id || `alert-${++this.nextId}`,
    }));
    const byId = new Map<string, AlertMessage>();
    for (const item of rawItems) {
      byId.set(item.id, item);
    }
    const items = [...byId.values()];
    const incomingIds = new Set(byId.keys());

    for (const id of incomingIds) {
      const existingTimer = this.timers.get(id);
      if (existingTimer !== undefined) {
        clearTimeout(existingTimer);
        this.timers.delete(id);
      }
    }

    this._messages.update(current => [...current.filter(m => !incomingIds.has(m.id)), ...items]);

    for (const item of items) {
      if (item.type !== 'error') {
        this.timers.set(
          item.id,
          setTimeout(() => this.close(item.id), AUTO_CLOSE_MS),
        );
      }
    }
  }

  success(title: string, detail?: string): void {
    this.open({ type: 'success', title, detail });
  }

  error(title: string, detail?: string): void {
    this.open({ type: 'error', title, detail });
  }

  info(title: string, detail?: string): void {
    this.open({ type: 'info', title, detail });
  }

  warning(title: string, detail?: string): void {
    this.open({ type: 'warning', title, detail });
  }

  /**
   * The warnings a committed mutation answered with. With a fallback title, each shows under it, its body the message's
   * title, then its detail; without one, each shows as it came. No messages show nothing.
   */
  warnings(messages: readonly MessageDto[] | null | undefined, fallbackTitle?: string | null): void {
    const items = (messages ?? []).map(warning =>
      fallbackTitle
        ? {
            ...warning,
            type: 'warning' as const,
            title: fallbackTitle,
            detail: [warning.title, warning.detail].filter(Boolean).join(' ') || undefined,
          }
        : { ...warning, type: 'warning' as const },
    );

    if (items.length > 0) {
      this.open(items);
    }
  }

  /** Closes one message by id, or every message when no id is given. */
  close(id?: string): void {
    const ids = id === undefined ? [...this.timers.keys(), ...this._messages().map(m => m.id)] : [id];

    for (const each of ids) {
      const timer = this.timers.get(each);
      if (timer !== undefined) {
        clearTimeout(timer);
        this.timers.delete(each);
      }
    }

    this._messages.update(current => (id === undefined ? [] : current.filter(m => m.id !== id)));
  }
}
