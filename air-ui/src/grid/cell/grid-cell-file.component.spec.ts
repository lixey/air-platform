import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../../tests/axe';
import { formModel } from '../../../tests/form-model';
import { bindGrid } from '../../../tests/grid-binding';
import { navModel } from '../../../tests/nav-model';
import { uploadLimits } from '../../../tests/upload-limits';
import { provideDynamicComponents } from '../../core/dynamic-component/provide-dynamic-components';
import type { CellModel, GridCellFileModel, GridColumnDto, GridModel, GridNavUploadModel } from '../../core/generated/contracts';
import { FILE_VIEWER_SOURCE, type FileViewerSource } from '../../file/file-viewer';
import { OverlayService } from '../../overlay/overlay.service';
import { UI_COMPONENTS } from '../../ui-components';
import type { GridRow } from '../grid-row';
import { GridService } from '../grid.service';
import type { GridCellContext } from './grid-cell-context';
import { GridCellFileComponent } from './grid-cell-file.component';
import { GridCellOutletComponent } from './grid-cell-outlet.component';

const cellModel: GridCellFileModel = { kind: 'GridCellFile' };

const column: GridColumnDto = {
  path: 'name',
  label: 'Name',
  type: 'text',
  isSortable: true,
  isFilterable: true,
  isResizable: true,
  isSticky: false,
  isStickyRight: false,
  cell: cellModel,
  isHidden: false,
  isDisabled: false,
  isRequired: false,
};

const file = (name: string, contentType: string, extra: Partial<Record<string, unknown>> = {}): GridRow => ({
  id: 1,
  name,
  contentType,
  size: 20000,
  createdAt: '2026-09-25T10:00:00Z',
  url: `/api/platform-files/token-${name}`,
  ...extra,
});

/** The file cell drawn for the row, under the column. */
function drawn(row: GridRow, target: GridColumnDto = column): HTMLElement {
  const fixture = TestBed.createComponent(GridCellFileComponent);
  fixture.componentRef.setInput('cell', { row, column: target, value: row['name'], index: 0 });
  fixture.componentRef.setInput('model', cellModel);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('GridCellFileComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it("draws the name as a link to the row's url that opens in a new browsing context with neither opener nor referrer", () => {
    const cell = drawn(file('plan.pdf', 'application/pdf'));
    const link = cell.querySelector('a');

    expect(link?.getAttribute('href')).toBe('/api/platform-files/token-plan.pdf');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link?.textContent?.replace(/\s+/g, ' ').trim()).toBe('plan.pdf (opens in a new tab)');
    expect(link?.querySelector('.sr-only')?.textContent).toBe('(opens in a new tab)');
  });

  it('shows a thumbnail loaded lazily from the url for each raster image a link serves inline', () => {
    for (const [name, type] of [
      ['logo.png', 'image/png'],
      ['photo.jpg', 'image/jpeg'],
      ['anim.gif', 'image/gif'],
      ['shot.webp', 'image/webp'],
    ]) {
      const thumbnail = drawn(file(name, type)).querySelector('img');

      expect(thumbnail?.getAttribute('src'), type).toBe(`/api/platform-files/token-${name}`);
      expect(thumbnail?.getAttribute('loading'), type).toBe('lazy');
      expect(thumbnail?.getAttribute('alt'), type).toBe('');
    }
  });

  it('shows no thumbnail for SVG, PDF or any other type, only the link', () => {
    for (const [name, type] of [
      ['diagram.svg', 'image/svg+xml'],
      ['plan.pdf', 'application/pdf'],
      ['notes.txt', 'text/plain'],
    ]) {
      const cell = drawn(file(name, type));

      expect(cell.querySelector('img'), type).toBeNull();
      expect(cell.querySelector('a')?.textContent?.replace(/\s+/g, ' ').trim(), type).toBe(`${name} (opens in a new tab)`);
    }
  });

  it('draws the name as plain text in a row without a url, and in a disabled column', () => {
    for (const cell of [
      drawn(file('logo.png', 'image/png', { url: null })),
      drawn(file('logo.png', 'image/png'), { ...column, isDisabled: true }),
    ]) {
      expect(cell.querySelector('a')).toBeNull();
      expect(cell.querySelector('img')).toBeNull();
      expect(cell.textContent?.trim()).toBe('logo.png');
    }
  });
});

@Component({
  imports: [GridCellOutletComponent],
  template: `
    <div ui-grid-cell-outlet [cell]="cell()" [model]="model()" fallback="GridCellText"></div>
  `,
})
class OutletHostComponent {
  readonly cell = input.required<GridCellContext>();
  readonly model = input.required<CellModel>();
}

describe("GridCellFileComponent painted by the row's hue", () => {
  afterEach(() => TestBed.resetTestingModule());

  it('keeps the link and the thumbnail inside the pill of the hue', () => {
    TestBed.configureTestingModule({
      providers: [provideDynamicComponents(UI_COMPONENTS), GridService, provideHttpClient(), provideHttpClientTesting()],
    });
    const model: GridCellFileModel = { kind: 'GridCellFile', colorPath: 'color' };
    const row = file('logo.png', 'image/png', { color: 'teal' });
    const fixture = TestBed.createComponent(OutletHostComponent);
    fixture.componentRef.setInput('cell', { row, column: { ...column, cell: model }, value: row['name'], index: 0 });
    fixture.componentRef.setInput('model', model);
    fixture.detectChanges();
    const pill = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[class*="hue-"]');

    expect(pill?.classList).toContain('hue-teal');
    expect(pill?.querySelector('ui-grid-cell-file a')?.getAttribute('href')).toBe('/api/platform-files/token-logo.png');
    expect(pill?.querySelector('ui-grid-cell-file img')).not.toBeNull();
  });

  it('passes axe as a link with a thumbnail and as plain text', async () => {
    await expectAccessible(drawn(file('logo.png', 'image/png')));
    TestBed.resetTestingModule();
    await expectAccessible(drawn(file('report.pdf', 'application/pdf', { url: null })));
  });
});

describe('GridCellFileComponent opening the viewer', () => {
  afterEach(() => TestBed.resetTestingModule());

  const page = [
    file('logo.png', 'image/png', { id: 1, thumbUrl: '/api/platform-files/thumb-logo.png' }),
    file('plan.pdf', 'application/pdf', { id: 2 }),
    file('scan.png', 'image/png', { id: 3 }),
    file('diagram.svg', 'image/svg+xml', { id: 4 }),
  ];

  /** Clicks the thumbnail of the row and answers the model and the source the viewer was opened with. */
  function view(row: GridRow): { model: unknown; source: FileViewerSource } {
    const open = vi.spyOn(TestBed.inject(OverlayService), 'open');
    const cell = drawn(row);
    const thumbnail = cell.querySelector<HTMLElement>('[data-file-view]');
    expect(thumbnail?.getAttribute('aria-label')).toBe(`Open ${String(row['name'])}`);
    thumbnail?.click();
    expect(open).toHaveBeenCalledTimes(1);
    const [model, , injector] = open.mock.calls[0];
    return { model: model.layout?.root.children?.[0]?.model, source: (injector as NonNullable<typeof injector>).get(FILE_VIEWER_SOURCE) };
  }

  it("opens the viewer at its row over the page's rows that show a thumbnail, and asks the page again for fresh links", () => {
    TestBed.configureTestingModule({ providers: [GridService, provideHttpClient(), provideHttpClientTesting()] });
    const grid = TestBed.inject(GridService);
    bindGrid(grid).data.set({ rows: page, page: 1, pageSize: 25, hasMore: false, total: page.length });
    const reload = vi.spyOn(grid, 'reload');

    const { model, source } = view(page[2]);

    expect(model).toEqual({ kind: 'FileViewerOverlay', key: 3 });
    expect(source.files().map(f => f.name)).toEqual(['logo.png', 'scan.png']);
    expect(source.canDelete()).toBe(false);
    source.refresh();
    expect(reload).toHaveBeenCalled();
  });

  const service = 'sample-report-file-grid';
  const gridModel = (upload: GridNavUploadModel | null): GridModel => ({
    kind: 'Grid',
    actionNav: navModel('actions'),
    headers: [],
    service,
    pageSize: 25,
    isSelectable: true,
    isReadOnly: false,
    focusesSearch: false,
    columns: [],
    nav: { ...navModel(), end: upload ? [upload] : [] },
    mutationTexts: formModel().mutationTexts,
  });
  const upload: GridNavUploadModel = {
    kind: 'GridNavUpload',
    key: 'upload',
    limits: uploadLimits({ accept: ['image/png'], extensions: ['.png'], maxBytes: 1024, sourceMaxBytes: 1024 }),
    canReadMetadata: true,
  };

  /** The grid holding the page under the model, its requests answered by the test. */
  function gridOf(model: GridModel): { grid: GridService; http: HttpTestingController } {
    TestBed.configureTestingModule({ providers: [GridService, provideHttpClient(), provideHttpClientTesting()] });
    const grid = TestBed.inject(GridService);
    const binding = bindGrid(grid);
    binding.model.set(model);
    binding.data.set({ rows: page, page: 1, pageSize: 25, hasMore: false, total: page.length });
    return { grid, http: TestBed.inject(HttpTestingController) };
  }

  it("gives the viewer's Info fields and the metadata where the grid's nav offers the upload, saved and reloaded through the grid", async () => {
    const { grid, http } = gridOf(gridModel(upload));
    const reload = vi.spyOn(grid, 'reload');
    const { source } = view(page[2]);

    expect(source.canEditInfo()).toBe(true);
    expect(source.canReadMetadata()).toBe(true);
    const info = source.info(source.files()[1]);
    const asked = http.expectOne(`/api/${service}/get-info`);
    expect(asked.request.body).toEqual(expect.objectContaining({ id: 3 }));
    asked.flush({ id: 3, ver: 1, name: 'scan.png', contentType: 'image/png', size: 20000, createdAt: '2026-09-25T10:00:00Z' });
    expect((await info).name).toBe('scan.png');

    const saved = source.saveInfo(source.files()[1], { name: 'Scan', description: null, ver: 1 });
    const save = http.expectOne(`/api/${service}/set-info`);
    expect(save.request.body).toEqual(
      expect.objectContaining({ selectedIds: [3], selectedVersions: [1], name: 'Scan', description: null }),
    );
    save.flush({ count: 1, outcome: 'committed' });
    expect(await saved).toBe(true);
    expect(reload).toHaveBeenCalled();
  });

  it("gives the viewer's Info as text and no metadata where the grid offers no upload", () => {
    gridOf(gridModel(null));
    const { source } = view(page[0]);

    expect(source.canEditInfo()).toBe(false);
    expect(source.canReadMetadata()).toBe(false);
  });

  it('opens the viewer over its own file outside a grid', async () => {
    const { source } = view(page[0]);

    expect(source.files().map(f => f.name)).toEqual(['logo.png']);
    expect(source.canEditInfo()).toBe(false);
    await expect(source.info(source.files()[0])).rejects.toThrow();
  });
});
