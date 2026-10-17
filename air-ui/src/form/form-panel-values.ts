import type { FormTab } from '../core/generated/contracts';
import { panelValue } from '../panels/panels-context';
import type { FormService } from './form.service';

/**
 * The form of a layout, published by the `form` panel for the nav's elements standing in
 * another panel of the same layout: `FormNavSave`, `FormNavSaveClose` and `FormNavSaveOpen` act
 * on it. A nested form in a page has a layout and a value of its own.
 */
export const FORM = panelValue<FormService | null>('form', null);

/**
 * The key of the form tab showing. The nav's `NavTab` elements write it; the `form` panel
 * shows the elements of that tab and keeps the others mounted and hidden. Its name is the
 * server's `FormTab.Group`.
 */
export const FORM_TAB = panelValue('formTab' satisfies FormTab, '');

/**
 * The key of the record tab: the elements declared before the first tab, whose button in the
 * nav is the record caption. What the `form` panel shows while the shared value names nothing.
 */
export const RECORD_TAB = 'record' satisfies FormTab;
