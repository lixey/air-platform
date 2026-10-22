import { defineComponents } from './core/dynamic-component/component-map';
import type { FileUploadLimitsDto, GridColumnDto, GridModel, NavElementModel, PlatformKind } from './core/generated/contracts';
import { DatePickerOverlayComponent } from './datepicker/date-picker-overlay.component';
import { FileUploadOverlayComponent } from './file/file-upload-overlay.component';
import type { ShownFile } from './file/file-upload-queue';
import { FileViewerOverlayComponent } from './file/file-viewer-overlay.component';
import { FormCheckboxSetComponent } from './form/element/form-checkbox-set.component';
import { FormCheckboxTreeComponent } from './form/element/form-checkbox-tree.component';
import { FormCheckboxComponent } from './form/element/form-checkbox.component';
import { FormDateComponent } from './form/element/form-date.component';
import { FormDatetimeComponent } from './form/element/form-datetime.component';
import { FormFilesComponent } from './form/element/form-files.component';
import { FormInfoComponent } from './form/element/form-info.component';
import { FormInputComponent } from './form/element/form-input.component';
import { FormMoneyComponent } from './form/element/form-money.component';
import { FormNumberComponent } from './form/element/form-number.component';
import { FormPercentComponent } from './form/element/form-percent.component';
import { FormPhoneComponent } from './form/element/form-phone.component';
import { FormSelectComponent } from './form/element/form-select.component';
import { FormTextareaComponent } from './form/element/form-textarea.component';
import { FormTimeZoneComponent } from './form/element/form-time-zone.component';
import { FormTimeComponent } from './form/element/form-time.component';
import { FormAskComponent } from './form/form-ask.component';
import { FormChildScreenComponent } from './form/form-child-screen.component';
import { FormComponent } from './form/form.component';
import { FormNavActionComponent } from './form/nav/form-nav-action.component';
import { FormNavSaveCloseComponent } from './form/nav/form-nav-save-close.component';
import { FormNavSaveOpenComponent } from './form/nav/form-nav-save-open.component';
import { FormNavSaveComponent } from './form/nav/form-nav-save.component';
import { GridCellCheckboxComponent } from './grid/cell/grid-cell-checkbox.component';
import { GridCellCoverComponent } from './grid/cell/grid-cell-cover.component';
import { GridCellDateComponent } from './grid/cell/grid-cell-date.component';
import { GridCellDatetimeComponent } from './grid/cell/grid-cell-datetime.component';
import { GridCellEmailComponent } from './grid/cell/grid-cell-email.component';
import { GridCellFileComponent } from './grid/cell/grid-cell-file.component';
import { GridCellMoneyComponent } from './grid/cell/grid-cell-money.component';
import { GridCellNumberComponent } from './grid/cell/grid-cell-number.component';
import { GridCellOpenFormComponent } from './grid/cell/grid-cell-open-form.component';
import { GridCellPercentComponent } from './grid/cell/grid-cell-percent.component';
import { GridCellPhoneComponent } from './grid/cell/grid-cell-phone.component';
import { GridCellPickerComponent } from './grid/cell/grid-cell-picker.component';
import { GridCellReferenceComponent } from './grid/cell/grid-cell-reference.component';
import { GridCellSelectComponent } from './grid/cell/grid-cell-select.component';
import { GridCellTextComponent } from './grid/cell/grid-cell-text.component';
import { GridCellTimeComponent } from './grid/cell/grid-cell-time.component';
import { GridCellUrlComponent } from './grid/cell/grid-cell-url.component';
import { GridColumnFilterOverlayComponent } from './grid/filter/grid-column-filter-overlay.component';
import { GridDateRangeOverlayComponent } from './grid/filter/grid-date-range-overlay.component';
import { GridFiltersOverlayComponent } from './grid/filter/grid-filters-overlay.component';
import type { GridRowKey } from './grid/grid-row';
import { GridComponent } from './grid/grid.component';
import { GridHeaderCheckboxComponent } from './grid/header/grid-header-checkbox.component';
import { GridHeaderDateComponent } from './grid/header/grid-header-date.component';
import { GridHeaderDefaultComponent } from './grid/header/grid-header-default.component';
import { GridHeaderLabelComponent } from './grid/header/grid-header-label.component';
import { GridHeaderReferenceComponent } from './grid/header/grid-header-reference.component';
import { GridHeaderSelectionComponent } from './grid/header/grid-header-selection.component';
import { GridHeaderToggleComponent } from './grid/header/grid-header-toggle.component';
import { GridNavActionComponent } from './grid/nav/grid-nav-action.component';
import { GridNavAddComponent } from './grid/nav/grid-nav-add.component';
import { GridNavNamedFilterGroupComponent } from './grid/nav/grid-nav-named-filter-group.component';
import { GridNavNamedFilterComponent } from './grid/nav/grid-nav-named-filter.component';
import { GridNavPickerComponent } from './grid/nav/grid-nav-picker.component';
import { GridNavSearchComponent } from './grid/nav/grid-nav-search.component';
import { GridNavStatusComponent } from './grid/nav/grid-nav-status.component';
import { GridNavUploadComponent } from './grid/nav/grid-nav-upload.component';
import { GridPickerOverlayComponent } from './grid/nav/grid-picker-overlay.component';
import { LayoutAgentComponent } from './layout/layout-agent.component';
import { LayoutMegaMenuBodyComponent } from './layout/layout-mega-menu-body.component';
import { LayoutMenuComponent } from './layout/layout-menu.component';
import { LayoutRouteScreenComponent } from './layout/layout-route-screen.component';
import { LayoutTabsComponent } from './layout/layout-tabs.component';
import { NavCloseComponent } from './nav/element/nav-close.component';
import { NavLabelComponent } from './nav/element/nav-label.component';
import { NavLinkComponent } from './nav/element/nav-link.component';
import { NavMenuComponent } from './nav/element/nav-menu.component';
import { NavOverlayComponent } from './nav/element/nav-overlay.component';
import { NavSearchComponent } from './nav/element/nav-search.component';
import { NavSeparatorComponent } from './nav/element/nav-separator.component';
import { NavTabComponent } from './nav/element/nav-tab.component';
import { NavTabsComponent } from './nav/element/nav-tabs.component';
import { NavToggleComponent } from './nav/element/nav-toggle.component';
import { NavMenuBodyComponent } from './nav/nav-menu-body.component';
import { NavComponent } from './nav/nav.component';
import { OverlayConfirmComponent } from './overlay/overlay-confirm.component';
import { OverlayDirtyComponent } from './overlay/overlay-dirty.component';
import { SelectOverlayComponent } from './select/select-overlay.component';

/** The shell's side menu, a panel the frame composes. */
export interface LayoutMenuModel {
  kind: 'LayoutMenu';
}

/** The shell's AI agent chat panel, between the menu and the work area. */
export interface LayoutAgentModel {
  kind: 'LayoutAgent';
}

/** The strip of the shell's open tabs, a panel the frame composes. */
export interface LayoutTabsModel {
  kind: 'LayoutTabs';
}

/** The screen the route names, a panel the frame composes; the routed component itself. */
export interface LayoutRouteScreenModel {
  kind: 'LayoutRouteScreen';
}

/** The body of the mega menu, a panel of the overlay the frame opens. */
export interface LayoutMegaMenuBodyModel {
  kind: 'LayoutMegaMenuBody';
}

/** The body of the select's overlay: the grid it picks from, read from the select's store. */
export interface SelectOverlayModel {
  kind: 'SelectOverlay';
}

/** The body of a picker's list: the source's grid model and the page it opens with. */
export interface GridPickerOverlayModel {
  kind: 'GridPickerOverlay';
  grid: GridModel;
  page: unknown;
}

/** The body of the date picker's overlay: the picker of the temporal control that opened it. */
export interface DatePickerOverlayModel {
  kind: 'DatePickerOverlay';
}

/** The body of a nav's menu: the elements of the nav's `more` part, or those of a `NavMenu`. */
export interface NavMenuBodyModel {
  kind: 'NavMenuBody';
  elements: readonly NavElementModel[];
  /** Whether an element that asks closes the menu: a `NavMenu` says so, the More menu does not. */
  closesOnAction?: boolean;
}

/** The body of a column's filter overlay, over the column it filters. */
export interface GridColumnFilterOverlayModel {
  kind: 'GridColumnFilterOverlay';
  column: GridColumnDto;
}

/** The body of a date column's range menu, over the column it filters. */
export interface GridDateRangeOverlayModel {
  kind: 'GridDateRangeOverlay';
  column: GridColumnDto;
}

/**
 * The body of the upload dialog: the file grid each file is posted to, the parent the files go under, the grid's
 * `FileUploadLimitsDto` (the accepted types, their extensions, the largest size posted and the largest picked image
 * taken, the most files and bytes of a record, its image rules and the server's bounds of every upload), the files the
 * grid shows at the opening, which take the record's room, and the title a committed upload's warnings take.
 */
export interface FileUploadOverlayModel extends FileUploadLimitsDto {
  kind: 'FileUploadOverlay';
  service: string;
  parentService?: string | null;
  parentId?: number | null;
  shownFiles: readonly ShownFile[];
  committedWarningTitle?: string | null;
}

/**
 * The body of the file viewer: the file it opens at. The files it walks, and what it may do with them, come from its
 * opener through `FILE_VIEWER_SOURCE`, a live sequence no model could carry.
 */
export interface FileViewerOverlayModel {
  kind: 'FileViewerOverlay';
  key: GridRowKey;
}

/** The header of a column that names none: the label, the sort and the filter. */
export interface GridHeaderDefaultModel {
  kind: 'GridHeaderDefault';
}

/** The header of the selection column: the box that selects the page. */
export interface GridHeaderSelectionModel {
  kind: 'GridHeaderSelection';
}

/** The panels only the library composes: the server never sends them. */
interface ClientPanelKinds {
  LayoutMenu: LayoutMenuModel;
  LayoutAgent: LayoutAgentModel;
  LayoutTabs: LayoutTabsModel;
  LayoutRouteScreen: LayoutRouteScreenModel;
  LayoutMegaMenuBody: LayoutMegaMenuBodyModel;
  SelectOverlay: SelectOverlayModel;
  DatePickerOverlay: DatePickerOverlayModel;
  GridColumnFilterOverlay: GridColumnFilterOverlayModel;
  GridDateRangeOverlay: GridDateRangeOverlayModel;
  GridPickerOverlay: GridPickerOverlayModel;
  NavMenuBody: NavMenuBodyModel;
  FileUploadOverlay: FileUploadOverlayModel;
  FileViewerOverlay: FileViewerOverlayModel;
}

/** The column headers only the library composes. */
interface ClientHeaderKinds {
  GridHeaderDefault: GridHeaderDefaultModel;
  GridHeaderSelection: GridHeaderSelectionModel;
}

/* eslint-disable @typescript-eslint/no-empty-interface, @typescript-eslint/no-empty-object-type
   -- an augmentation adds its kinds by extending */
declare module './core/generated/contracts' {
  interface PanelKinds extends ClientPanelKinds {}
  interface HeaderKinds extends ClientHeaderKinds {}
}
/* eslint-enable @typescript-eslint/no-empty-interface, @typescript-eslint/no-empty-object-type */

/** The kinds only the library composes: the server never sends them. */
export type ClientKind = keyof ClientPanelKinds | keyof ClientHeaderKinds;

/** Every kind the library draws, the platform's and its own, each under the component named after it. */
export const UI_COMPONENTS = defineComponents({
  LayoutMenu: LayoutMenuComponent,
  LayoutAgent: LayoutAgentComponent,
  LayoutTabs: LayoutTabsComponent,
  LayoutRouteScreen: LayoutRouteScreenComponent,
  LayoutMegaMenuBody: LayoutMegaMenuBodyComponent,
  Nav: NavComponent,
  NavLink: NavLinkComponent,
  NavMenu: NavMenuComponent,
  NavOverlay: NavOverlayComponent,
  NavTab: NavTabComponent,
  NavTabs: NavTabsComponent,
  NavToggle: NavToggleComponent,
  NavSearch: NavSearchComponent,
  NavClose: NavCloseComponent,
  NavSeparator: NavSeparatorComponent,
  NavLabel: NavLabelComponent,
  NavMenuBody: NavMenuBodyComponent,
  OverlayConfirm: OverlayConfirmComponent,
  OverlayDirty: OverlayDirtyComponent,
  SelectOverlay: SelectOverlayComponent,
  DatePickerOverlay: DatePickerOverlayComponent,
  Grid: GridComponent,
  GridNavAction: GridNavActionComponent,
  GridNavAdd: GridNavAddComponent,
  GridNavNamedFilter: GridNavNamedFilterComponent,
  GridNavNamedFilterGroup: GridNavNamedFilterGroupComponent,
  GridNavSearch: GridNavSearchComponent,
  GridNavStatus: GridNavStatusComponent,
  GridNavUpload: GridNavUploadComponent,
  GridNavPicker: GridNavPickerComponent,
  GridPickerOverlay: GridPickerOverlayComponent,
  FileUploadOverlay: FileUploadOverlayComponent,
  FileViewerOverlay: FileViewerOverlayComponent,
  GridCellText: GridCellTextComponent,
  GridCellCheckbox: GridCellCheckboxComponent,
  GridCellNumber: GridCellNumberComponent,
  GridCellDate: GridCellDateComponent,
  GridCellDatetime: GridCellDatetimeComponent,
  GridCellTime: GridCellTimeComponent,
  GridCellOpenForm: GridCellOpenFormComponent,
  GridCellPicker: GridCellPickerComponent,
  GridCellReference: GridCellReferenceComponent,
  GridCellSelect: GridCellSelectComponent,
  GridCellFile: GridCellFileComponent,
  GridCellCover: GridCellCoverComponent,
  GridCellMoney: GridCellMoneyComponent,
  GridCellPercent: GridCellPercentComponent,
  GridCellPhone: GridCellPhoneComponent,
  GridCellEmail: GridCellEmailComponent,
  GridCellUrl: GridCellUrlComponent,
  GridHeaderDefault: GridHeaderDefaultComponent,
  GridHeaderSelection: GridHeaderSelectionComponent,
  GridHeaderCheckbox: GridHeaderCheckboxComponent,
  GridHeaderDate: GridHeaderDateComponent,
  GridHeaderLabel: GridHeaderLabelComponent,
  GridHeaderToggle: GridHeaderToggleComponent,
  GridHeaderReference: GridHeaderReferenceComponent,
  GridColumnFilterOverlay: GridColumnFilterOverlayComponent,
  GridDateRangeOverlay: GridDateRangeOverlayComponent,
  GridFiltersOverlay: GridFiltersOverlayComponent,
  Form: FormComponent,
  FormAsk: FormAskComponent,
  FormChildScreen: FormChildScreenComponent,
  FormInput: FormInputComponent,
  FormNumber: FormNumberComponent,
  FormMoney: FormMoneyComponent,
  FormPercent: FormPercentComponent,
  FormPhone: FormPhoneComponent,
  FormTextarea: FormTextareaComponent,
  FormCheckbox: FormCheckboxComponent,
  FormDate: FormDateComponent,
  FormDatetime: FormDatetimeComponent,
  FormTime: FormTimeComponent,
  FormSelect: FormSelectComponent,
  FormTimeZone: FormTimeZoneComponent,
  FormCheckboxSet: FormCheckboxSetComponent,
  FormCheckboxTree: FormCheckboxTreeComponent,
  FormInfo: FormInfoComponent,
  FormFiles: FormFilesComponent,
  FormNavAction: FormNavActionComponent,
  FormNavSave: FormNavSaveComponent,
  FormNavSaveClose: FormNavSaveCloseComponent,
  FormNavSaveOpen: FormNavSaveOpenComponent,
}) satisfies Record<PlatformKind | ClientKind, unknown>;
