import { Component, DestroyRef, ElementRef, inject, Injector, model, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { AlertService } from '../../app/alert/alert.service';
import { shownByCaller } from '../../app/alert/error-alert.interceptor';
import { SearchBoxComponent } from '../../controls/search-box.component';
import { Api } from '../../core/api';
import type { GridNavPickerModel, GridRequest, GridServiceEndpoints, ScreenDto } from '../../core/generated/contracts';
import { classifyMutationFailure, refusalOf } from '../../core/mutation-failure';
import { TextsService } from '../../core/texts.service';
import { OverlayService } from '../../overlay/overlay.service';
import { serviceMenu } from '../../panels/service-menu';
import { BAR_HEIGHT } from '../../select/select-list';
import type { SelectOption } from '../../select/select-option';
import type { SelectField } from '../../select/select-source';
import { SelectService } from '../../select/select.service';
import type { GridPickerOverlayModel } from '../../ui-components';
import { GridService } from '../grid.service';
import { GridActionsService } from './grid-actions.service';
import { isOwnerCurrent } from './grid-nav-mutation';

/** The key of the picker's menu and of its layout. */
const GRID_PICKER_OVERLAY = 'grid.picker';

/** The most a picker's list takes, in pixels, and the width its menu states; a narrower window bounds it by its frame. */
const MOST_PICKER_WIDTH = 1920;

/**
 * The least room a picker's list needs under its field to hang there, in pixels: its header and 240 pixels of table.
 * With less it stands in a window of its own.
 */
const LEAST_PICKER_ROOM = BAR_HEIGHT + 240;

/** The route segment of the read a picker asks its source by: its own key of the grid's endpoint map. */
const GRID_DATA = 'get-data' satisfies keyof GridServiceEndpoints;

/** The parent a request names. */
type ParentOfRequest = Pick<GridRequest, 'parentService' | 'parentId'>;

/** The parent the grid's own request names, as a source that reads it is asked. */
function parentOf({ parentService, parentId }: GridRequest): ParentOfRequest {
  return { parentService, parentId };
}

/**
 * The grid's picker: a field that adds a row to the grid it stands in by a pick from a source grid. A moment after the
 * typing stops, a trimmed text of the model's `minLength` asks the source (`referenceService`) as a picker for its
 * first page and opens the answer's grid in a menu from the field (`GridPickerOverlay`), under the answer's nav: the
 * search box of that nav takes the caret and is the field's remote (`SelectField`), holding the field's own text and
 * handing its pause and its keys to the field's handlers. While the list is open a new text narrows it through the
 * list's own grid, and one shorter than the least length keeps it open with no row, saying that length in the rows'
 * place (`SelectService.shortOf`); the arrows move the list's active row, `Enter` picks it, `Escape` closes the list
 * alone and `Tab` closes it and moves on from the field. A list that closes otherwise returns focus to the field. The
 * list opens as wide as the window's frame, at most `MOST_PICKER_WIDTH`: the menu states that one width, whatever the
 * columns come to, and keeps it, so no page moves an edge of the card. The list's grid fits the columns at its opening
 * to the page the picker read, and while the list is open they only widen: every page it reads leaves each fitted
 * column at the wider of the width it has and the fit of that page; nothing narrows until the list closes, and the next
 * opening fits afresh. Its columns share the card by their widths (`GridTableComponent`), and columns wider than the card
 * draw their own widths and scroll sideways. The menu hangs down only (`OverlayOpenOptions.hangsDownOnly`): the list
 * stands under the field where the least room it states (`LEAST_PICKER_ROOM`) fits there, as tall as its rows and at
 * most the room under its top edge, and otherwise in a window of its own as tall as the window's frame, never above
 * the field, so its search box keeps its place while
 * the rows come and go; it is worked alike in both forms. A pick, by `Enter` or a click on a row, closes the list and posts the row's
 * key to the model's `method` through the grid's `GridActionsService`: a committed one empties the field for the next
 * text, a refused one keeps it.
 * A source that reads the parent of the grid the element stands in (`readsParent`) is asked under it: the first
 * request and every request of the list name that grid's `parentService` and `parentId`; any other source is named none.
 * The state of the list is a `SelectService` of the element's own, which the grid in the list finds by injection.
 * Outside a grid there is nothing to add to, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-picker',
  imports: [SearchBoxComponent],
  providers: [SelectService],
  template: `
    @if (grid) {
      <ui-search-box
        class="w-56"
        [(text)]="text"
        [placeholder]="model()?.label"
        [controls]="menu.id()"
        [popup]="{ kind: 'grid', isOpen: menu.isOpen() }"
        (searched)="search($event)"
        (keydown)="onKeydown($event)"
      />
    }
  `,
})
export class GridNavPickerComponent {
  protected readonly grid = inject(GridService, { optional: true });
  private readonly actions = inject(GridActionsService, { optional: true });
  private readonly select = inject(SelectService);
  private readonly api = inject(Api);
  private readonly alert = inject(AlertService);
  private readonly texts = inject(TextsService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  /** The list's card, one at a time. */
  protected readonly menu = inject(OverlayService).menu();

  private readonly box = viewChild(SearchBoxComponent);
  private readonly anchor = viewChild(SearchBoxComponent, { read: ElementRef<HTMLElement> });

  readonly model = model<GridNavPickerModel | null>();

  /** The text in the field, and in the search box of the open list. */
  protected readonly text = signal('');

  /** What the list's search box works the field by: the one text and the field's two handlers. */
  private readonly field: SelectField = {
    text: this.text,
    searched: text => void this.search(text),
    keydown: event => this.onKeydown(event),
  };

  constructor() {
    this.select.committed.pipe(takeUntilDestroyed()).subscribe(options => void this.pick(options[0]));
    this.destroyRef.onDestroy(() => this.menu.close());
  }

  /**
   * What the field, or the search box of its open list, said after its pause: a text shorter than the least length
   * asks nothing, opens nothing and tells an open list the length it waits for, which stays open without rows; an
   * open list is narrowed by its own grid; otherwise the source is asked for its first page, whose answer opens the
   * list while the field still holds that text and the grid shows what it showed.
   * A failure is reported.
   */
  protected async search(typed: string): Promise<void> {
    const picker = this.model();
    const grid = this.grid;
    const text = typed.trim();
    if (!picker || !grid) {
      return;
    }
    if (text.length < picker.minLength) {
      if (this.menu.isOpen()) {
        this.select.setShortOf(picker.minLength);
      }
      return;
    }
    if (this.menu.isOpen()) {
      this.select.search(text);
      return;
    }

    const parent = picker.readsParent ? parentOf(grid.request()) : null;
    const isCurrent = isOwnerCurrent(grid, this.destroyRef);
    const applies = (): boolean => isCurrent() && this.text().trim() === text && !this.menu.isOpen();
    try {
      const answer = await firstValueFrom(
        this.api.postTo<GridServiceEndpoints, typeof GRID_DATA>(
          picker.referenceService,
          GRID_DATA,
          { page: 1, isReferencePicker: true, search: text, ...parent },
          shownByCaller(),
        ),
      );
      if (applies()) {
        this.open(picker, text, answer, parent);
      }
    } catch (error: unknown) {
      if (applies()) {
        const refusal = refusalOf(classifyMutationFailure(error), this.texts);
        this.alert.error(refusal.title, refusal.detail);
      }
    }
  }

  /**
   * The keys of the field and of the list's search box while the list is open; `Home`, `End` and the page keys stay
   * the box's.
   */
  protected onKeydown(event: KeyboardEvent): void {
    if (!this.menu.isOpen()) {
      return;
    }
    switch (event.key) {
      case 'ArrowDown':
        this.select.moveActive(1);
        break;
      case 'ArrowUp':
        this.select.moveActive(-1);
        break;
      case 'Enter':
        this.select.pickActive();
        break;
      case 'Escape':
        this.menu.close();
        break;
      case 'Tab':
        // The field takes the caret back and the browser moves focus on from it: the key is not taken
        this.box()?.focus();
        this.menu.close();
        return;
      default:
        return;
    }
    // The caret stays, and the host's Escape leaves the overlay under the list open
    event.preventDefault();
  }

  /**
   * Opens the list over the grid the answer's one panel carries, kept only when the panel's kind says it is one: the
   * grid starts from the text its page was read by and names `parent` in its own requests. Its columns are handed as
   * the source answered them: the list's grid fits them to that page at its opening, drawn fitted from its first paint,
   * and widens them by every page it reads. The menu states one width, `MOST_PICKER_WIDTH`, whatever the columns come
   * to, which the window's frame bounds, and as its height `LEAST_PICKER_ROOM`, the room the list needs under the field
   * to hang there; hanging, it is as tall as its rows and at most the room under its top edge.
   */
  private open(picker: GridNavPickerModel, text: string, answer: ScreenDto | null, parent: ParentOfRequest | null): void {
    const grid = answer?.layout?.root.children?.[0]?.model;
    const anchor = this.anchor()?.nativeElement ?? null;
    if (grid?.kind !== 'Grid' || !anchor) {
      return;
    }

    this.select.setSource({
      label: picker.label ?? '',
      service: picker.referenceService,
      isPicker: true,
      parent,
      field: this.field,
    });
    const body: GridPickerOverlayModel = {
      kind: 'GridPickerOverlay',
      grid: { ...grid, initialSearch: text },
      page: answer?.data,
    };
    const menu = serviceMenu(GRID_PICKER_OVERLAY, body, { token: SelectService, instance: this.select, parent: this.injector });
    this.menu.open(
      { ...menu.model, width: `${MOST_PICKER_WIDTH}px`, height: `${LEAST_PICKER_ROOM}px` },
      anchor,
      menu.injector,
      picker.label || this.texts.of('SearchOptions'),
      { keepsOpenerFocus: true, hangsDownOnly: true },
    );
  }

  /**
   * A pick: the list closes and the field takes focus back, which a press on a row may have moved; the key is posted,
   * and a committed pick empties the field for the next text.
   */
  private async pick(option: SelectOption | undefined): Promise<void> {
    const picker = this.model();
    this.menu.close();
    this.box()?.focus();
    if (!picker || !option || !this.actions) {
      return;
    }
    if ((await this.actions.pick(picker, option.id)) && !this.destroyRef.destroyed) {
      this.menu.close();
      this.text.set('');
      this.box()?.focus();
    }
  }
}
