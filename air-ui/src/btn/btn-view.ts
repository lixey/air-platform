import type { ButtonSize, ButtonVariant } from '../core/generated/contracts';
import type { UiIcon } from '../icon/ui-icon';
import type { UiColor } from '../theme/ui-color';

/** A button with every field decided. */
export interface BtnView {
  label: string;
  title: string;
  icon: UiIcon | null;
  color: UiColor;
  variant: ButtonVariant;
  size: ButtonSize;
  badge: string | number | null;
  disabled: boolean;
  selected: boolean;
}

export const BTN_DEFAULTS: BtnView = {
  label: '',
  title: '',
  icon: null,
  color: 'primary',
  variant: 'soft',
  size: 'medium',
  badge: null,
  disabled: false,
  selected: false,
};
