import type { Injector } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { withIdempotencyKey } from '../app/auth/credentials.interceptor';
import type { Api } from '../core/api';
import type { ActionInputDto, FormAskModel, FormServiceEndpoints, MessageDto, OverlayConfirmModel } from '../core/generated/contracts';
import { isRecord } from '../core/json';
import { classifyMutationFailure } from '../core/mutation-failure';
import { UncertainIdempotency } from '../core/uncertain-idempotency';
import { stableJson } from '../form/form-value';
import type { AskService } from '../overlay/ask.service';
import type { ConfirmationService } from '../overlay/confirmation.service';

/** The route segment that resolves server-computed default values for an action's `Ask` dialog. */
export const ACTION_INPUT_ENDPOINT = 'get-action-input' satisfies keyof FormServiceEndpoints;

export interface ActionPromptOptions {
  readonly service: string;
  readonly method: string;
  readonly label?: string | null;
  readonly ask?: FormAskModel | null;
  readonly confirm?: OverlayConfirmModel | null;
  readonly asks: AskService;
  readonly confirmation: ConfirmationService;
  readonly injector: Injector;
  readonly isCurrent: () => boolean;
  readonly buildDefaultParams?: () => Promise<object | null>;
  readonly onDefaultsLoading?: (loading: boolean) => void;
  readonly reportDefaultsError: (error: unknown) => void;
  readonly submit: (value: Readonly<Record<string, unknown>>) => Promise<readonly MessageDto[]>;
}

/**
 * Shared client execution engine for declared Form and Grid actions (including status transitions, pickers, and
 * deletions): manages bounded 16-slot `Idempotency-Key` headers via `Api` (reusing a key on retry after an `'unknown'`
 * network failure for the identical route and payload, and rotating keys after commit, refusal, conflict, or payload
 * change) and resolves `get-action-input` defaults before opening an `Ask` dialog.
 */
export class ActionRunner {
  private readonly uncertain = new UncertainIdempotency();

  constructor(private readonly api: Api) {}

  /** Clears any retained uncertain idempotency keys (e.g. when a form resets). */
  reset(): void {
    this.uncertain.clear();
  }

  /**
   * Posts an action mutation via `Api` with an `Idempotency-Key` header. If a previous post of the exact same
   * `service`, `method`, and `body` ended with an `'unknown'` outcome, reuses its key so the server can safely replay a
   * committed response; any other outcome drops the key so a subsequent invocation receives a fresh UUID.
   */
  async post<T>(service: string, method: string, body?: unknown): Promise<T> {
    const fingerprint = `${service}/${method}\n${stableJson(body)}`;
    const key = this.uncertain.take(fingerprint);
    try {
      const answer = await firstValueFrom(this.api.call<T>(service, method, body, withIdempotencyKey(key)));
      if (isRecord(answer) && answer['outcome'] === 'unknown') {
        this.uncertain.keep(fingerprint, key);
      }
      return answer;
    } catch (error: unknown) {
      if (classifyMutationFailure(error).outcome === 'unknown') {
        this.uncertain.keep(fingerprint, key);
      }
      throw error;
    }
  }

  /** Fetches initial field defaults for an action's `Ask` dialog from `<service>/get-action-input`. */
  async fetchDefaults(service: string, method: string, params: object): Promise<Readonly<Record<string, unknown>>> {
    const answer = await firstValueFrom(
      this.api.call<ActionInputDto | null>(service, ACTION_INPUT_ENDPOINT, { ...params, method }, shownByCaller()),
    );
    return answer?.value ?? {};
  }

  /**
   * Runs the interactive prompt lifecycle of a Form or Grid action: fetches server defaults when `ask.hasDefaults` is
   * set, opens the `Ask` dialog (keeping it open while refusal messages target its fields) or asks `confirm`, and
   * invokes `submit` with the dialog's values (or `{}` when there is no `Ask` dialog).
   */
  async prompt(options: ActionPromptOptions): Promise<void> {
    const { ask, confirm, label, service, method, asks, confirmation, injector, isCurrent, submit } = options;
    if (ask) {
      let given: Readonly<Record<string, unknown>> | undefined;
      if (ask.hasDefaults) {
        const params = options.buildDefaultParams ? await options.buildDefaultParams() : {};
        if (params === null || !isCurrent()) {
          return;
        }
        options.onDefaultsLoading?.(true);
        try {
          given = await this.fetchDefaults(service, method, params);
        } catch (error: unknown) {
          if (isCurrent()) {
            options.reportDefaultsError(error);
          }
          return;
        } finally {
          options.onDefaultsLoading?.(false);
        }
        if (!isCurrent()) {
          return;
        }
      }
      await asks.ask({ ...ask, title: ask.title ?? label }, submit, injector, given);
      return;
    }

    if (!confirm || (await confirmation.confirm(confirm))) {
      await submit({});
    }
  }
}
