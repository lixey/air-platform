import { Component, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { expectAccessible } from '../../tests/axe';
import type { UploadAccept } from './file-upload-call';
import { FileZoneDirective } from './file-zone.directive';

const accept: UploadAccept = { accept: ['image/png'], extensions: ['.png'] };

@Component({
  imports: [FileZoneDirective],
  template: `
    <section
      aria-label="Files"
      [uiFileZone]="limits()"
      (uiFileZoneTaken)="taken.push($event)"
      #zone="uiFileZone"
      [attr.data-dragging]="zone.isDragging() || null"
    >
      <p>Drop files here</p>
    </section>
  `,
})
class ZoneHost {
  readonly limits = signal<UploadAccept | null>(accept);
  readonly taken: File[][] = [];
}

const fileOf = (name: string): File => new File([new Uint8Array(1)], name, { type: 'image/png' });

/** A drag event of the type given, carrying the transfer given, as the browser dispatches it over the zone. */
function dragOf(type: string, transfer: object = { types: ['Files'] }, relatedTarget: EventTarget | null = null): Event {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'dataTransfer', { value: transfer });
  Object.defineProperty(event, 'relatedTarget', { value: relatedTarget });
  return event;
}

describe('FileZoneDirective', () => {
  let fixture: ComponentFixture<ZoneHost>;

  const zone = (): HTMLElement => fixture.nativeElement.querySelector('section') as HTMLElement;
  const isDragging = (): boolean => zone().hasAttribute('data-dragging');

  function dispatch(event: Event, target: HTMLElement = zone()): Event {
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  beforeEach(() => {
    fixture = TestBed.createComponent(ZoneHost);
    fixture.detectChanges();
  });
  afterEach(() => TestBed.resetTestingModule());

  it('marks the zone while files are dragged over it and prevents the default, so the browser does not open them', () => {
    const over = dispatch(dragOf('dragover'));

    expect(over.defaultPrevented).toBe(true);
    expect(isDragging()).toBe(true);
  });

  it('clears the mark when the drag leaves the zone, not when it moves onto something inside it', () => {
    dispatch(dragOf('dragover'));
    const inside = zone().querySelector('p') as HTMLElement;

    dispatch(dragOf('dragleave', { types: ['Files'] }, inside));
    expect(isDragging()).toBe(true);
    dispatch(dragOf('dragleave', { types: ['Files'] }, document.body));
    expect(isDragging()).toBe(false);
    dispatch(dragOf('dragover'));
    dispatch(dragOf('dragleave'));
    expect(isDragging()).toBe(false);
  });

  it('hands the dropped files over in their order, clearing the mark', () => {
    const files = [fileOf('a.png'), fileOf('b.png')];
    dispatch(dragOf('dragover'));

    const dropped = dispatch(dragOf('drop', { types: ['Files'], files }));

    expect(dropped.defaultPrevented).toBe(true);
    expect(fixture.componentInstance.taken).toEqual([files]);
    expect(isDragging()).toBe(false);
  });

  it('takes nothing without limits, and never a drag that started on the page', () => {
    const pageDrag = { types: ['application/x-ui-page-drag', 'Files'], files: [fileOf('a.png')] };

    expect(dispatch(dragOf('dragover', pageDrag)).defaultPrevented).toBe(false);
    expect(dispatch(dragOf('drop', pageDrag)).defaultPrevented).toBe(false);
    fixture.componentInstance.limits.set(null);
    fixture.detectChanges();
    expect(dispatch(dragOf('dragover')).defaultPrevented).toBe(false);
    expect(dispatch(dragOf('drop', { types: ['Files'], files: [fileOf('b.png')] })).defaultPrevented).toBe(false);

    expect(fixture.componentInstance.taken).toEqual([]);
    expect(isDragging()).toBe(false);
  });

  it('passes axe', async () => {
    await expectAccessible(fixture.nativeElement as HTMLElement);
  });
});
