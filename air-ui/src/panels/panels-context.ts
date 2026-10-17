import { inject, Service, signal, type WritableSignal } from '@angular/core';

/**
 * A value the panels of one layout share.
 *
 * Declare it once, next to its type, and every panel that imports it is talking about the
 * same thing — no strings passed around, no wiring between panels that have never heard
 * of each other.
 *
 * ```ts
 * export const SELECTED_TASK = panelValue<Task | null>('selectedTask', null);
 * ```
 *
 * The name is the value's identity in its layout: declare it once and import it. Only text meets by name — the
 * server's tabs and search write the text value their model names, and a panel reading that name declares a text
 * value of it — so a second declaration of a name that is not text on both sides is refused ({@link PanelValueError}).
 */
export interface PanelValue<T> {
  readonly name: string;
  readonly initial: T;
}

/** Declares a shared value, once per name. See {@link PanelValue}. */
export function panelValue<T>(name: string, initial: T): PanelValue<T> {
  return { name, initial };
}

/** Two declarations of one name that are not both text: the one signal behind the name would hold two types. */
export class PanelValueError extends Error {
  constructor(name: string) {
    super(`Two panel values are named "${name}": declare it once and import it; only text meets by name.`);
    this.name = 'PanelValueError';
  }
}

/** Text meets by name: what the server's tabs and search write. */
const isText = (value: PanelValue<unknown>): boolean => typeof value.initial === 'string';

/**
 * How panels talk to each other.
 *
 * Every layout has one, provided by `<ui-panels>`. Ask it for a shared value and you get
 * an ordinary writable signal — read it to follow along, set it to tell everyone else.
 * There is no publisher and no subscriber, so it reads the same in both directions.
 *
 * ```ts
 * // The list, on selecting a row:
 * private readonly selected = inject(PanelsContext).of(SELECTED_TASK);
 * protected select(task: Task) { this.selected.set(task); }
 *
 * // The details panel, in the same layout:
 * protected readonly task = inject(PanelsContext).of(SELECTED_TASK);   // follows the list
 * protected rename(title: string) { this.task.update(t => ({...t!, title})); }  // and back
 * ```
 *
 * **Levels.** A layout can hold a panel that is itself a layout, with its own definition
 * from its own endpoint. Each of those has its own context, and `level`, `parent` and
 * `root` say where you are:
 *
 * ```ts
 * const here = inject(PanelsContext);
 * here.of(FILTER);        // shared with the panels beside me
 * here.root.of(USER);     // shared with every panel on the screen, however deep
 * here.level;             // 0 at the top
 * ```
 *
 * Reaching *down* into a nested layout is deliberately not offered: publish at `root`
 * instead, and every level sees it without anyone having to find anyone.
 *
 * A name is declared once: asking for a second declaration of a name that is not text on both sides throws a
 * {@link PanelValueError} naming it, in production too, like a component map that names no kind.
 *
 * Values live as long as the layout is on screen. Nothing here is persisted — this is what
 * the panels are doing right now, not how the user arranged them.
 */
@Service({ autoProvided: false })
export class PanelsContext {
  private readonly values = new Map<string, WritableSignal<unknown>>();

  /** The first declaration asked for under each name. */
  private readonly declared = new Map<string, PanelValue<unknown>>();

  /** The context of the layout this one sits inside, or `null` at the top. */
  readonly parent = inject(PanelsContext, { optional: true, skipSelf: true });

  /** 0 for the outermost layout on the screen, 1 for one nested inside it, and so on. */
  readonly level: number = this.parent ? this.parent.level + 1 : 0;

  /** The outermost context on the screen. `this`, at the top. */
  readonly root: PanelsContext = this.parent ? this.parent.root : this;

  /**
   * The shared signal behind a declared value, created the first time it is asked for. Throws a {@link PanelValueError}
   * for a second declaration of the name unless both are text.
   */
  of<T>(value: PanelValue<T>): WritableSignal<T> {
    const first = this.declared.get(value.name);

    if (!first) {
      this.declared.set(value.name, value);
    } else if (first !== value && !(isText(first) && isText(value))) {
      throw new PanelValueError(value.name);
    }

    let shared = this.values.get(value.name);

    if (!shared) {
      shared = signal<unknown>(value.initial);
      this.values.set(value.name, shared);
    }

    // Sound: one declaration per name, or text on both sides
    return shared as WritableSignal<T>;
  }
}
