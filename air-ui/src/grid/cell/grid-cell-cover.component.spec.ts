import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../../tests/axe';
import type { GridCellCoverModel, GridColumnDto, GridCoverDto } from '../../core/generated/contracts';
import { FILE_VIEWER_SOURCE, type FileViewerSource } from '../../file/file-viewer';
import { OverlayService } from '../../overlay/overlay.service';
import type { GridRow } from '../grid-row';
import { GridCellCoverComponent } from './grid-cell-cover.component';

const service = 'ui-project-file-grid';
const cellModel: GridCellCoverModel = { kind: 'GridCellCover', service, parentService: 'ui-project-form' };

const column: GridColumnDto = {
  path: 'idCover',
  label: 'Cover',
  type: 'text',
  isSortable: false,
  isFilterable: false,
  isResizable: true,
  isSticky: false,
  isStickyRight: false,
  cell: cellModel,
  isHidden: false,
  isDisabled: false,
  isRequired: false,
};

const cover: GridCoverDto = {
  id: 12,
  name: 'site.jpg',
  contentType: 'image/jpeg',
  url: '/api/platform-files/site',
  thumbUrl: '/api/platform-files/site-thumb',
};

const files = [
  { id: 11, ver: 1, name: 'plan.pdf', contentType: 'application/pdf', size: 2000, url: '/f/11' },
  { id: 12, ver: 1, name: 'site.jpg', contentType: 'image/jpeg', size: 3000, url: '/f/12', thumbUrl: '/f/12t', isCover: true },
  { id: 13, ver: 1, name: 'wall.png', contentType: 'image/png', size: 4000, url: '/f/13' },
  { id: 14, ver: 1, name: 'act.pdf', contentType: 'application/pdf', size: 5000, url: '/f/14' },
];

async function settle(): Promise<void> {
  for (let i = 0; i < 3; i++) {
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
  }
}

describe('GridCellCoverComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<GridCellCoverComponent>;

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;

  function drawn(row: GridRow, target: GridColumnDto = column): HTMLElement {
    fixture = TestBed.createComponent(GridCellCoverComponent);
    fixture.componentRef.setInput('cell', { row, column: target, value: row[target.path ?? ''], index: 0 });
    fixture.componentRef.setInput('model', cellModel);
    fixture.detectChanges();
    return host();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it("draws the cover's thumbnail as a button that names it, and an empty cell for a row without a cover", () => {
    const cell = drawn({ id: 7, name: 'Site', idCover: cover });
    const open = cell.querySelector('[data-cover-open]');

    expect(open?.tagName).toBe('BUTTON');
    expect(open?.getAttribute('aria-label')).toBe('Open site.jpg');
    expect(open?.querySelector('img')?.getAttribute('src')).toBe('/api/platform-files/site-thumb');
    expect(open?.querySelector('img')?.getAttribute('loading')).toBe('lazy');

    const empty = drawn({ id: 8, name: 'Bare', idCover: null });
    expect(empty.textContent?.trim()).toBe('');
    expect(empty.querySelector('button, img')).toBeNull();
  });

  it('draws an image without a thumbnail from its url, and any other file as the badge of its extension', () => {
    expect(
      drawn({ id: 7, idCover: { ...cover, thumbUrl: null } })
        .querySelector('img')
        ?.getAttribute('src'),
    ).toBe('/api/platform-files/site');

    const pdf = drawn({ id: 7, idCover: { ...cover, name: 'act.pdf', contentType: 'application/pdf', thumbUrl: null } });
    expect(pdf.querySelector('img')).toBeNull();
    expect(pdf.querySelector('[data-cover-open]')?.textContent?.trim()).toBe('PDF');
  });

  it('draws the picture and opens nothing in a disabled column', () => {
    const cell = drawn({ id: 7, idCover: cover }, { ...column, isDisabled: true });

    expect(cell.querySelector('button')).toBeNull();
    expect(cell.querySelector('img')).not.toBeNull();
  });

  it("asks the file grid's page under the row's record and opens the viewer at the cover over the record's files", async () => {
    const open = vi.spyOn(TestBed.inject(OverlayService), 'open');
    drawn({ id: 7, name: 'Site', idCover: cover });

    host().querySelector<HTMLElement>('[data-cover-open]')?.click();
    fixture.detectChanges();
    expect(host().querySelector('[data-cover-open]')?.getAttribute('aria-busy')).toBe('true');
    const request = http.expectOne(`/api/${service}/get-data`);
    expect(request.request.body).toEqual({ parentService: 'ui-project-form', parentId: 7, isDataOnly: true, page: 1 });
    request.flush({ data: { rows: files, page: 1, pageSize: 300, hasMore: false, total: 4 } });
    await settle();

    expect(open).toHaveBeenCalledTimes(1);
    const [model, anchor, injector] = open.mock.calls[0];
    expect(anchor).toBeNull();
    expect(model.layout?.root.children?.[0]?.model).toEqual({ kind: 'FileViewerOverlay', key: 12 });
    const source: FileViewerSource = (injector as NonNullable<typeof injector>).get(FILE_VIEWER_SOURCE);
    expect(source.files().map(file => file.name)).toEqual(['plan.pdf', 'site.jpg', 'wall.png', 'act.pdf']);
    expect(source.canDelete()).toBe(false);
    // Info reads the file grid under the record, as text: a cover column carries neither the upload nor the right
    expect(source.canEditInfo()).toBe(false);
    expect(source.canReadMetadata()).toBe(false);
    const info = source.info(source.files()[1]);
    const asked = http.expectOne(`/api/${service}/get-info`);
    expect(asked.request.body).toEqual({ parentService: 'ui-project-form', parentId: 7, id: 12 });
    asked.flush({ id: 12, ver: 1, name: 'site.jpg', contentType: 'image/jpeg', size: 3000, createdAt: '2026-09-25T10:00:00Z' });
    expect((await info).name).toBe('site.jpg');

    // A failed image asks the page again, for fresh links
    source.refresh();
    expect(source.isRefreshing()).toBe(true);
    http
      .expectOne(`/api/${service}/get-data`)
      .flush({ data: { rows: files.map(f => ({ ...f, url: `${f.url}?fresh` })), page: 1, pageSize: 300, hasMore: false, total: 4 } });
    await settle();
    expect(source.isRefreshing()).toBe(false);
    expect(source.files()[1].url).toBe('/f/12?fresh');
  });

  it('reads the key of the member the column stands on', async () => {
    const open = vi.spyOn(TestBed.inject(OverlayService), 'open').mockImplementation(() => ({}) as ReturnType<OverlayService['open']>);
    drawn({ id: 1, projectId: 9, projectIdCover: cover }, { ...column, path: 'projectIdCover' });

    host().querySelector<HTMLElement>('[data-cover-open]')?.click();

    const request = http.expectOne(`/api/${service}/get-data`);
    expect(request.request.body).toEqual(expect.objectContaining({ parentId: 9 }));
    request.flush({ data: { rows: files, page: 1, pageSize: 300, hasMore: false, total: 4 } });
    await settle();
    expect(open).toHaveBeenCalledTimes(1);
  });

  it('passes axe with a cover and without one', async () => {
    await expectAccessible(drawn({ id: 7, idCover: cover }));
    await expectAccessible(drawn({ id: 8, idCover: null }));
  });
});
