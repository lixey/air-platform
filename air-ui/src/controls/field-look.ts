import { InjectionToken } from '@angular/core';
import type { FieldLook } from '../theme/css-classes';

/** How the controls under it draw their field: a cell's editor provides `'cell'`; a control without it draws a box. */
export const FIELD_LOOK = new InjectionToken<FieldLook>('FIELD_LOOK');
