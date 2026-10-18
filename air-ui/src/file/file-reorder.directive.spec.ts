import { Component, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../tests/axe';
import { FileReorderDirective, type FileReorderItem, type FileReorderMove } from './file-reorder.directive';

const five: readonly FileReorderItem[] = ['a.png', 'b.png', 'c.png', 'd.png', 'e.png'].map((name, index) => ({ key: index + 1, name }));

@Component({
  imports: [FileReorderDirective],
  template: `
    <ul
      role="list"
      class="grid"
      [uiFileReorder]="items()"
      [uiFileReorderDisabled]="disabled()"
      (uiFileReorderMoved)="moved.push($event)"
      #reorder="uiFileReorder"
    >
      @for (item of reorder.order(); track item.key) {
        <li [attr.data-reorder-item]="item.key" [attr.data-moving]="reorder.movingKey() === item.key || null">
          <span>{{ item.name }}</span>
          <button type="button" class="touch-none" [attr.data-reorder-handle]="item.key" [attr.aria-label]="item.name"></button>
        </li>
      }
    </ul>
    <p role="status" aria-live="polite">{{ reorder.announcement() }}</p>
  `,
  host: { '(keydown)': 'outer($event)' },
})
class ReorderHost {
  readonly items = signal<readonly FileReorderItem[]>(five);
  readonly disabled = signal(false);
  readonly moved: FileReorderMove[] = [];
  readonly outer = vi.fn<(event: KeyboardEvent) => void>();
}

describe('FileReorderDirective', () => {
  let fixture: ComponentFixture<ReorderHost>;

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const shown = (): string[] => [...host().querySelectorAll('li span')].map(name => name.textContent?.trim() ?? '');
  const said = (): string => host().querySelector('[role="status"]')?.textContent?.trim() ?? '';
  const handle = (key: number): HTMLElement => host().querySelector(`[data-reorder-handle="${key}"]`) as HTMLElement;
  const item = (key: number): HTMLElement => host().querySelector(`[data-reorder-item="${key}"]`) as HTMLElement;

  function press(key: string, target: HTMLElement): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  /** A pointer event of `type` at the point, a finger's unless said otherwise. */
  function pointer(type: string, target: Element, x: number, y: number, extra: { pointerType?: string; button?: number } = {}): Event {
    const event = new MouseEvent(type, { bubbles: true, cancelable: true, clientX: x, clientY: y, button: extra.button ?? 0 });
    Object.defineProperty(event, 'pointerId', { value: 7 });
    Object.defineProperty(event, 'pointerType', { value: extra.pointerType ?? 'touch' });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  /** Lays the files out in one row of 100-pixel cells, since jsdom lays out nothing. */
  function layOut(): void {
    for (const element of host().querySelectorAll<HTMLElement>('[data-reorder-item]')) {
      element.getBoundingClientRect = () => {
        const index = [...host().querySelectorAll('[data-reorder-item]')].indexOf(element);
        return { left: index * 100, right: index * 100 + 99, top: 0, bottom: 99, width: 99, height: 99, x: index * 100, y: 0 } as DOMRect;
      };
    }
  }

  beforeEach(() => {
    fixture = TestBed.createComponent(ReorderHost);
    fixture.detectChanges();
    document.body.appendChild(host());
  });
  afterEach(() => {
    host().remove();
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('takes a file by Space, carries it earlier by Left and Up, and drops it by Space before the file it now precedes', async () => {
    handle(3).focus();

    press(' ', handle(3));
    expect(said()).toBe('c.png, position 3 of 5');
    expect(item(3).hasAttribute('data-moving')).toBe(true);

    press('ArrowLeft', handle(3));
    expect(said()).toBe('c.png, position 2 of 5');
    expect(shown()).toEqual(['a.png', 'c.png', 'b.png', 'd.png', 'e.png']);
    press('ArrowUp', handle(3));
    expect(said()).toBe('c.png, position 1 of 5');
    press('ArrowLeft', handle(3));
    expect(said()).toBe('c.png, position 1 of 5');
    await fixture.whenStable();
    expect(document.activeElement).toBe(handle(3));

    const drop = press(' ', handle(3));

    expect(drop.defaultPrevented).toBe(true);
    expect(fixture.componentInstance.moved).toEqual([{ key: 3, beforeKey: 1, keys: [3, 1, 2, 4, 5] }]);
    expect(item(3).hasAttribute('data-moving')).toBe(false);
  });

  it('names no file to stand before when a file is carried last by Right and Down', () => {
    press(' ', handle(2));
    press('ArrowRight', handle(2));
    press('ArrowDown', handle(2));
    press('ArrowRight', handle(2));
    press('ArrowRight', handle(2));
    expect(said()).toBe('b.png, position 5 of 5');
    press(' ', handle(2));

    expect(fixture.componentInstance.moved).toEqual([{ key: 2, beforeKey: null, keys: [1, 3, 4, 5, 2] }]);
  });

  it('puts the file back by Escape, which closes nothing around it, and emits no move for a file dropped where it stood', () => {
    press(' ', handle(4));
    press('ArrowLeft', handle(4));

    const escape = press('Escape', handle(4));

    expect(escape.defaultPrevented).toBe(true);
    expect(fixture.componentInstance.outer.mock.calls.some(([event]) => event === escape)).toBe(false);
    expect(said()).toBe('d.png put back');
    expect(shown()).toEqual(['a.png', 'b.png', 'c.png', 'd.png', 'e.png']);

    press(' ', handle(4));
    press(' ', handle(4));
    expect(fixture.componentInstance.moved).toEqual([]);
  });

  it('puts the file back when Tab leaves its handle', () => {
    press(' ', handle(1));
    press('ArrowRight', handle(1));
    press('Tab', handle(1));

    expect(shown()).toEqual(['a.png', 'b.png', 'c.png', 'd.png', 'e.png']);
    expect(said()).toBe('a.png put back');
  });

  it('drags a file by a finger past the next one, the files making room, and drops it before the file it now precedes', () => {
    layOut();

    pointer('pointerdown', handle(1), 50, 50);
    pointer('pointermove', host().querySelector('ul') as Element, 52, 51);
    expect(item(1).hasAttribute('data-moving')).toBe(false);

    pointer('pointermove', host().querySelector('ul') as Element, 150, 50);
    expect(item(1).hasAttribute('data-moving')).toBe(true);
    expect(shown()).toEqual(['b.png', 'a.png', 'c.png', 'd.png', 'e.png']);
    expect(said()).toBe('a.png, position 2 of 5');
    layOut();
    pointer('pointerup', host().querySelector('ul') as Element, 150, 50);

    expect(fixture.componentInstance.moved).toEqual([{ key: 1, beforeKey: 3, keys: [2, 1, 3, 4, 5] }]);
  });

  it('refuses the gestures of the browser on the handle alone, so the page scrolls elsewhere', () => {
    const down = pointer('pointerdown', handle(2), 150, 50);
    const elsewhere = pointer('pointerdown', item(3).querySelector('span') as Element, 250, 50);

    expect(down.defaultPrevented).toBe(true);
    expect(elsewhere.defaultPrevented).toBe(false);
    expect(handle(2).className).toContain('touch-none');
    expect(item(2).className).not.toContain('touch-none');
  });

  it('puts the file back when the pointer is cancelled, and moves nothing for a tap', () => {
    layOut();
    pointer('pointerdown', handle(1), 50, 50);
    pointer('pointermove', handle(1), 250, 50);
    expect(shown()[2]).toBe('a.png');

    pointer('pointercancel', handle(1), 250, 50);
    expect(shown()).toEqual(['a.png', 'b.png', 'c.png', 'd.png', 'e.png']);
    expect(said()).toBe('a.png put back');

    pointer('pointerdown', handle(2), 150, 50);
    pointer('pointerup', handle(2), 150, 50);
    expect(fixture.componentInstance.moved).toEqual([]);
  });

  it("takes nothing while disabled, nor by a mouse's other buttons", () => {
    layOut();
    pointer('pointerdown', handle(1), 50, 50, { pointerType: 'mouse', button: 2 });
    pointer('pointermove', handle(1), 150, 50);
    expect(shown()[0]).toBe('a.png');

    fixture.componentInstance.disabled.set(true);
    fixture.detectChanges();
    press(' ', handle(1));
    press('ArrowRight', handle(1));
    expect(shown()[0]).toBe('a.png');
    expect(said()).toBe('');
  });

  it('passes axe while a file is carried', async () => {
    press(' ', handle(2));
    await expectAccessible(host());
  });
});
