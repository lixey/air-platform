import { HttpClient } from '@angular/common/http';
import { DestroyRef, inject, Injector, Service, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AlertService } from '../../app/alert/alert.service';
import { shownByCaller } from '../../app/alert/error-alert.interceptor';
import { withIdempotencyKey } from '../../app/auth/credentials.interceptor';
import { Api } from '../../core/api';
import { fetchFile, saveFile } from '../../core/download';
import type {
  FormAskModel,
  GridActionDto,
  GridDeleteDto,
  GridNavActionModel,
  GridNavPickerModel,
  GridServiceEndpoints,
  MessageDto,
} from '../../core/generated/contracts';
import type { IsCurrent } from '../../core/lifetime';
import { classifyMutationFailure, refusalDetailOf, refusalOf } from '../../core/mutation-failure';
import { TextsService } from '../../core/texts.service';
import { AskService, ownMessages } from '../../overlay/ask.service';
import { ConfirmationService } from '../../overlay/confirmation.service';
import { ScreenMutations, type ScreenOperation } from '../../screen/screen-mutations';
import type { GridRowKey } from '../grid-row';
import { GridService } from '../grid.service';
import { buildActionCall, type GridActionCall } from './grid-action-call';
import { gridNavMutation, isOwnerCurrent, type GridNavMutation } from './grid-nav-mutation';

/** The grid's deletion: the action whose method is this key of the grid's endpoint map, its own key and a persistent one. */
export const GRID_DELETE = 'delete' satisfies keyof GridServiceEndpoints;

/** The route segment of the read that answers the values an action's dialog opens with: its own key of the grid's map. */
export const GRID_ACTION_INPUT = 'get-action-input' satisfies keyof GridServiceEndpoints;

/** A deletion posted: its attempt, the rows it deletes, the screen's operation it holds and the service it posts to. */
interface Removal {
  readonly attempt: symbol;
  readonly ids: readonly GridRowKey[];
  readonly operation: ScreenOperation | undefined;
  readonly service: string;
}

/** What a call over the grid needs of what started it: where it posts, and the confirmation an action carries. */
type ActionTarget = Pick<GridNavActionModel, 'service' | 'method' | 'confirm'>;

/** One call of an action over the grid: what a plain action, the grid's deletion, a download and a pick differ by. */
interface ActionExchange<T> {
  readonly action: ActionTarget;
  /** Whether the grid still shows what it showed when the run began. */
  readonly isCurrent: IsCurrent;
  /** What the action's dialog asked. A call that carries it asks no confirmation: the dialog stood in its place. */
  readonly input?: Readonly<Record<string, unknown>>;
  /** Sends the call. */
  readonly send: (call: GridActionCall) => Promise<T>;
  /** Takes the answer of a call that still applies. */
  readonly apply: (answer: T, call: GridActionCall) => void;
  /** Takes the failure of a call that still applies; `call` is null when nothing was posted yet. */
  readonly fail: (error: unknown, call: GridActionCall | null) => void;
}

/**
 * The run of a grid's actions, in the scope of the grid and not of the button that started it: a button of the nav and
 * an item of a menu, which closes as the action starts, only start it here. It posts the grid's parameters with the
 * selected ids and the versions their rows showed to a method of a service, after the confirmation the action carries
 * when it carries one, shows the warnings of the answer's commit and reloads the grid's page afterwards. An action that
 * asks its input opens the dialog of it in place of the confirmation and posts the dialog's values in the same request;
 * a refusal at a field of the dialog stays in it. A dialog whose `ask` carries `hasDefaults` opens with the values the
 * grid's `get-action-input` answers for the selection, and does not open when that read fails or answers too late. A
 * download saves the file the method answers instead: nothing is reloaded and the selection stays. A pick of the grid's
 * picker posts the picked key the same way, as an action's input, and asks nothing first. A refusal is
 * reported under the server's title, else the platform's failed request, with what the server's messages say. An answer
 * applies while the grid shows what it showed when the action began, whether its button still stands or not.
 */
@Service({ autoProvided: false })
export class GridActionsService {
  private readonly grid = inject(GridService);
  private readonly http = inject(HttpClient);
  private readonly api = inject(Api);
  private readonly confirmation = inject(ConfirmationService);
  private readonly asks = inject(AskService);
  private readonly injector = inject(Injector);
  private readonly alert = inject(AlertService);
  private readonly mutations = inject(ScreenMutations, { optional: true });
  private readonly texts = inject(TextsService);
  private readonly destroyRef = inject(DestroyRef);

  /** The methods of the actions under way. */
  private readonly running = signal<ReadonlySet<string>>(new Set());

  /** The last pick whose outcome nobody knows, kept with its idempotency key for a pick of the same row by the same picker. */
  private uncertainPick: { readonly method: string; readonly pickedId: GridRowKey; readonly key: string } | null = null;

  /** Whether the action is under way: its button reads busy wherever it is drawn. */
  isRunning(action: Pick<GridNavActionModel, 'method'>): boolean {
    return this.running().has(action.method);
  }

  /**
   * Posts a pick of the grid's picker `method` under an idempotency key of its own, or under the key of the last pick of
   * the same row by the same picker when that one's outcome is unknown; resolves true when it committed. Nothing while
   * one is under way.
   */
  async pick(picker: Pick<GridNavPickerModel, 'method'>, pickedId: GridRowKey): Promise<boolean> {
    const service = this.grid.service();
    if (!service || this.isRunning(picker)) {
      return false;
    }

    const kept = this.uncertainPick;
    const key = kept?.method === picker.method && kept.pickedId === pickedId ? kept.key : crypto.randomUUID();
    // Any other answer, or a pick of another row, drops the key kept
    this.uncertainPick = null;
    const mutation = gridNavMutation(this.grid, this.destroyRef);
    let isCommitted = false;
    await this.during(
      picker,
      () =>
        this.perform<GridActionDto | null>({
          action: { service, method: picker.method },
          isCurrent: mutation.isCurrent,
          input: { pickedId },
          send: call => this.send(call, key),
          apply: (answer, call) => {
            isCommitted = true;
            this.adopt(answer, call, mutation);
          },
          fail: error => {
            if (classifyMutationFailure(error).outcome === 'unknown') {
              this.uncertainPick = { method: picker.method, pickedId, key };
            }
            this.report(error);
          },
        }),
      mutation.finish,
    );
    return isCommitted;
  }

  /** Runs the action over the grid: a download, the grid's deletion or a plain action, as its model says; nothing while it is under way. */
  async run(action: GridNavActionModel): Promise<void> {
    if (this.isRunning(action)) {
      return;
    }

    if (action.isDownload) {
      await this.download(action);
    } else if (action.method !== GRID_DELETE) {
      await this.mutate(action);
    } else {
      const attempt = this.grid.deletion.begin();
      if (attempt) {
        await this.remove(action, attempt);
      }
    }
  }

  /**
   * Runs an action that changes rows, other than the grid's deletion: after its confirmation, or through the dialog of
   * its input, which posts on every confirmation and stays open while a refusal names one of its fields. A dialog that
   * opens with the server's values waits for them, the action busy meanwhile, and does not open without them.
   */
  private async mutate(action: GridNavActionModel): Promise<void> {
    const mutation = gridNavMutation(this.grid, this.destroyRef);
    const ask = action.ask;
    await this.during(action, () => (ask ? this.ask(action, ask, mutation) : this.post(action, mutation)), mutation.finish);
  }

  /** Opens the dialog of the action's input, with the server's values when it opens with them: every confirmation posts. */
  private async ask(action: GridNavActionModel, ask: FormAskModel, mutation: GridNavMutation): Promise<void> {
    const given = ask.hasDefaults ? await this.defaultsOf(action, mutation) : undefined;
    if (given !== null) {
      await this.asks.ask({ ...ask, title: ask.title ?? action.label }, value => this.post(action, mutation, value), this.injector, given);
    }
  }

  /**
   * The values the action's dialog opens with, read for what the action would post without the dialog's values; null
   * when the dialog does not open: the read failed, which is reported, or the grid shows something else by now.
   */
  private async defaultsOf(action: GridNavActionModel, mutation: GridNavMutation): Promise<Readonly<Record<string, unknown>> | null> {
    const call = buildActionCall(action, this.grid.request(), this.grid.selection.selected());
    try {
      const answer = await firstValueFrom(
        this.api.postTo<GridServiceEndpoints, typeof GRID_ACTION_INPUT>(
          action.service,
          GRID_ACTION_INPUT,
          { ...call.body, method: action.method },
          shownByCaller(),
        ),
      );
      return mutation.isCurrent() ? (answer?.value ?? {}) : null;
    } catch (error: unknown) {
      if (mutation.isCurrent()) {
        this.report(error);
      }
      return null;
    }
  }

  /**
   * Posts the action with `input`, what its dialog asked: a committed answer clears the selection, shows its warnings,
   * tells the screen of a save of the grid's service for the ids posted, which no grid reads for, and reloads the page;
   * a failure is reported, but for its messages at the fields of the dialog, which are answered.
   */
  private async post(
    action: GridNavActionModel,
    mutation: GridNavMutation,
    input?: Readonly<Record<string, unknown>>,
  ): Promise<readonly MessageDto[]> {
    let own: readonly MessageDto[] = [];
    await this.perform<GridActionDto | null>({
      action,
      isCurrent: mutation.isCurrent,
      input,
      send: call => this.send(call),
      apply: (answer, call) => this.adopt(answer, call, mutation),
      fail: error => {
        own = action.ask ? ownMessages(action.ask, classifyMutationFailure(error).messages) : [];
        this.report(error, own);
      },
    });
    return own;
  }

  /** Sends the call of a plain action or of a pick, a pick under its idempotency `key`; the run reports its failure. */
  private send(call: GridActionCall, key?: string): Promise<GridActionDto | null> {
    const options = key === undefined ? shownByCaller() : withIdempotencyKey(key);
    return firstValueFrom(this.http.post<GridActionDto | null>(call.url, call.body, options));
  }

  /** The answer of a plain action: the selection cleared, its warnings, the form it names, the screen told, the page reloaded. */
  private adopt(answer: GridActionDto | null, call: GridActionCall, mutation: GridNavMutation): void {
    this.grid.selection.clear();
    this.alert.warnings(answer?.warnings, this.grid.model()?.mutationTexts.committedWarningTitle);
    if (answer?.openScreen) {
      const id = typeof answer.openId === 'number' ? answer.openId : null;
      this.grid.openForm(id, null, answer.openScreen);
    }
    const service = this.grid.service();
    if (mutation.operation && service) {
      this.mutations?.publish(mutation.operation, { service, operation: 'save', selectedIds: idsOf(call) });
    }
    this.grid.reload();
  }

  /** Posts the grid's deletion under its `attempt`, which takes the answer or the failure and ends with the post. */
  private async remove(action: GridNavActionModel, attempt: symbol): Promise<void> {
    const mutation = gridNavMutation(this.grid, this.destroyRef);
    await this.during(
      action,
      () =>
        this.perform<GridDeleteDto>({
          action,
          isCurrent: mutation.isCurrent,
          send: call =>
            firstValueFrom(
              this.api.postTo<GridServiceEndpoints, typeof GRID_DELETE>(action.service, GRID_DELETE, call.body, shownByCaller()),
            ),
          apply: (answer, call) =>
            this.deleted(answer, { attempt, ids: idsOf(call), operation: mutation.operation, service: action.service }),
          fail: (error, call) => this.grid.deletion.fail(attempt, idsOf(call), classifyMutationFailure(error)),
        }),
      () => {
        mutation.finish();
        this.grid.deletion.finish(attempt);
      },
    );
  }

  /**
   * The deletion's answer: a committed one removes the deleted rows from the selection, shows its warnings and tells
   * the screen, whose grids reload, or reloads this grid outside a screen; any other is the attempt's failure.
   */
  private deleted(answer: GridDeleteDto | null, removal: Removal): void {
    const { attempt, ids, operation, service } = removal;
    if (answer?.outcome !== 'committed') {
      this.grid.deletion.fail(attempt, ids, classifyMutationFailure(answer));
      return;
    }
    this.grid.deletion.commit(attempt);
    this.grid.selection.remove(ids);
    this.alert.warnings(answer.warnings, this.grid.model()?.mutationTexts.committedWarningTitle);
    if (operation) {
      this.mutations?.publish(operation, { service, operation: 'delete', selectedIds: ids });
    } else {
      this.grid.reload();
    }
  }

  /** A download changes nothing: no mutation is begun, the page is not reloaded and the selection is kept. */
  private async download(action: GridNavActionModel): Promise<void> {
    const isCurrent = isOwnerCurrent(this.grid, this.destroyRef);
    await this.during(action, () =>
      this.perform({
        action,
        isCurrent,
        send: call => fetchFile(this.http, call.url, call.body, action.label ?? action.method, shownByCaller()),
        apply: file => saveFile(file),
        fail: error => this.report(error),
      }),
    );
  }

  /**
   * The action is under way while `work` runs: at its end, whatever it was, `release` gives back what the run began and
   * the action is idle again, unless the grid is gone.
   */
  private async during(action: Pick<GridNavActionModel, 'method'>, work: () => Promise<unknown>, release?: () => void): Promise<void> {
    this.running.update(running => new Set(running).add(action.method));
    try {
      await work();
    } finally {
      release?.();
      if (!this.destroyRef.destroyed) {
        this.running.update(running => new Set([...running].filter(method => method !== action.method)));
      }
    }
  }

  /**
   * One call of the action, in this order: the confirmation the action carries is asked, unless the exchange carries the
   * input of a dialog, and a dismissal ends it; the call is built over the grid's request and selection only while the
   * exchange is current; the call is sent, and its answer applied while the exchange is current still. A failure of any
   * of these is taken under the same check, with the call it failed after, or dropped.
   */
  private async perform<T>(exchange: ActionExchange<T>): Promise<void> {
    const { action, isCurrent, input, send, apply, fail } = exchange;
    let call: GridActionCall | null = null;
    try {
      if (input === undefined && action.confirm && !(await this.confirmation.confirm(action.confirm))) {
        return;
      }
      if (!isCurrent()) {
        return;
      }
      call = buildActionCall(action, this.grid.request(), this.grid.selection.selected(), input);
      const answer = await send(call);
      if (isCurrent()) {
        apply(answer, call);
      }
    } catch (error: unknown) {
      if (isCurrent()) {
        fail(error, call);
      }
    }
  }

  /**
   * A refused action: the server's title, else the platform's failed request, over what the server's messages say, a
   * line each, a message after the label of the record it refuses when the server sent one. The messages of `own`, which the
   * action's dialog shows on its fields, are left out, and a failure they are all of is not reported.
   */
  private report(error: unknown, own: readonly MessageDto[] = []): void {
    const failure = classifyMutationFailure(error);
    const messages = failure.messages.filter(message => !own.includes(message));
    if (own.length && !messages.length) {
      return;
    }
    this.alert.error(refusalOf(failure, this.texts).title, refusalDetailOf(failure, messages.map(labelled), '\n'));
  }
}

/** A message as the nav says it: `label: text` under the label of the record it refuses, else as it came. */
function labelled(message: MessageDto): MessageDto {
  const said = message.title ?? message.detail;
  return message.recordLabel && said ? { ...message, title: `${message.recordLabel}: ${said}` } : message;
}

/** The ids a call posts, none for a call not built. */
function idsOf(call: GridActionCall | null): readonly GridRowKey[] {
  return [...(call?.body.selectedIds ?? [])];
}
