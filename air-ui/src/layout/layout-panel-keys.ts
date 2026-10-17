import type { FramePanel } from '../core/generated/contracts';

/**
 * The panel paths of the application frame, declared once: the frame composes them and the menu,
 * the tabs and the mega menu address them. The navigation panel is the server's `FramePanel`, which a screen's nav
 * toggles through the outermost layout. `ui:u{id}:panels:app.shell` remembers a user's arrangement
 * under these paths, so a change to one of them resets what that panel remembered — which is what
 * the move from `nav` to `menu` does once.
 */
export const MENU_PANEL = 'menu' satisfies FramePanel;
export const WORK_PANEL = 'work';
export const TABS_PANEL = 'work/tabs';
