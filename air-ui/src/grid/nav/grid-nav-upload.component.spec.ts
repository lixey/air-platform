import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentRef, createComponent, createEnvironmentInjector, EnvironmentInjector, type Injector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../../tests/axe';
import { formModel } from '../../../tests/form-model';
import { bindGrid } from '../../../tests/grid-binding';
import { navModel } from '../../../tests/nav-model';
import { uploadLimits } from '../../../tests/upload-limits';
import type { GridModel, GridNavUploadModel, OverlayDto } from '../../core/generated/contracts';
import { uploadOverlayOf } from '../../file/file-upload-overlay.component';
import { OverlayMenu } from '../../overlay/overlay-menu';
import { OverlayService } from '../../overlay/overlay.service';
import { SCREEN_ADDRESS } from '../../screen/screen-address';
import { GridService } from '../grid.service';
import { GridNavUploadComponent } from './grid-nav-upload.component';

const MIB = 1024 * 1024;
const service = 'sample-report-file-grid';
const gridModel: GridModel = {
  kind: 'Grid',
  actionNav: navModel('actions'),
  headers: [],
  service,
  pageSize: 25,
  isSelectable: true,
  isReadOnly: false,
  focusesSearch: false,
  columns: [],
  nav: navModel(),
  mutationTexts: formModel().mutationTexts,
};
const upload: GridNavUploadModel = {
  kind: 'GridNavUpload',
  key: 'upload',
  label: 'Upload',
  icon: 'import',
  variant: 'solid',
  limits: uploadLimits({ maxBytes: MIB, sourceMaxBytes: MIB }),
  canReadMetadata: false,
};

describe('GridNavUploadComponent', () => {
  let http: HttpTestingController;
  let injector: EnvironmentInjector;
  let grid: GridService;
  let component: ComponentRef<GridNavUploadComponent>;
  let closed: Subject<unknown>;
  let opened: ReturnType<typeof vi.fn>;

  const host = (): HTMLElement => component.location.nativeElement as HTMLElement;

  /** The requests the reload of the page posts, drained. */
  function reloads(): number {
    const pages = http.match(`/api/${service}/get-data`).length;
    http.match(r => r.url.startsWith(`/api/${service}/get-`));
    return pages;
  }

  beforeEach(() => {
    closed = new Subject<unknown>();
    opened = vi.fn(() => ({ closed }));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OverlayService, useValue: { open: opened, menu: () => new OverlayMenu({ open: opened } as unknown as OverlayService) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    injector = createEnvironmentInjector(
      [
        GridService,
        { provide: SCREEN_ADDRESS, useValue: signal({ service, params: { parentService: 'sample-report-form', parentId: 7 } }) },
      ],
      TestBed.inject(EnvironmentInjector),
    );
    grid = injector.get(GridService);
    bindGrid(grid).model.set(gridModel);
    bindGrid(grid).data.set({
      rows: [
        { id: 4, name: 'plan.pdf', size: 20000 },
        { id: 3, name: 'logo.png', size: 5000 },
      ],
      total: 2,
      page: 1,
      pageSize: 25,
      hasMore: false,
    });
    component = createComponent(GridNavUploadComponent, { environmentInjector: injector });
    component.instance.model.set(upload);
    component.changeDetectorRef.detectChanges();
  });
  afterEach(() => {
    component.destroy();
    injector.destroy();
    http.verify();
    TestBed.resetTestingModule();
  });

  function click(): void {
    (host().querySelector('ui-btn') as HTMLElement).dispatchEvent(new MouseEvent('click'));
  }

  it("opens the upload dialog over the grid's service, its parent, its limits and the files it shows, owned by the element's screen", () => {
    click();

    expect(opened).toHaveBeenCalledTimes(1);
    const [dialog, anchor, opener] = opened.mock.calls[0] as [OverlayDto, unknown, Injector];
    expect(dialog).toEqual(
      uploadOverlayOf({
        ...upload.limits,
        service,
        parentService: 'sample-report-form',
        parentId: 7,
        shownFiles: [
          { id: 4, size: 20000 },
          { id: 3, size: 5000 },
        ],
        committedWarningTitle: gridModel.mutationTexts.committedWarningTitle,
      }),
    );
    expect(anchor).toBeNull();
    expect(opener.get(GridService)).toBe(grid);
    expect(host().querySelector('ui-btn')?.getAttribute('aria-haspopup')).toBe('dialog');
  });

  it('reloads the page once the dialog closes', () => {
    click();
    expect(reloads()).toBe(0);

    closed.next(undefined);

    expect(reloads()).toBe(1);
  });

  it('leaves a grid that moved on meanwhile as it is', () => {
    click();
    bindGrid(grid).model.set({ ...gridModel });

    closed.next(undefined);

    expect(reloads()).toBe(0);
  });

  it('passes axe', async () => {
    document.body.appendChild(host());
    try {
      await expectAccessible(host());
    } finally {
      host().remove();
    }
  });
});
