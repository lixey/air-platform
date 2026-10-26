import { DOCUMENT } from '@angular/common';
import { computed, effect, inject, Service, signal } from '@angular/core';

/** The theme a user chooses: one of the two, or the one the browser's colour scheme names. */
export type ThemeMode = 'light' | 'dark' | 'system';

/**
 * The theme of the page: the mode the user chose, kept in the browser under `ui:theme`, and the theme it resolves to,
 * which puts the `dark` class on the document element and syncs `<meta name="theme-color">` with `--panel-page`. The
 * `system` mode follows the browser's colour scheme as it changes.
 */
@Service()
export class ThemeService {
  private readonly storageKey = 'ui:theme';
  private readonly document = inject(DOCUMENT);
  private readonly window = this.document.defaultView;

  private readonly _mode = signal<ThemeMode>(this.initialMode());
  readonly mode = this._mode.asReadonly();

  private readonly isSystemDark = signal<boolean>(this.watchSystemDark());

  readonly effectiveTheme = computed<'light' | 'dark'>(() => {
    const currentMode = this.mode();
    if (currentMode === 'system') {
      return this.isSystemDark() ? 'dark' : 'light';
    }
    return currentMode;
  });

  readonly isDark = computed<boolean>(() => this.effectiveTheme() === 'dark');

  constructor() {
    // Before the first change detection, so the page never paints in the other theme.
    this.applyTheme();
    effect(() => this.applyTheme());
  }

  /** Chooses the mode and keeps it in the browser for the next visit. */
  setMode(newMode: ThemeMode): void {
    this._mode.set(newMode);
    this.window?.localStorage.setItem(this.storageKey, newMode);
  }

  /** Switches to the theme opposite the one shown, leaving the `system` mode. */
  toggle(): void {
    this.setMode(this.isDark() ? 'light' : 'dark');
  }

  private applyTheme(): void {
    this.document.documentElement.classList.toggle('dark', this.isDark());
    const meta = this.document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
    if (meta && this.window) {
      const raw = this.window.getComputedStyle(this.document.documentElement).getPropertyValue('--panel-page').trim();
      if (raw) {
        meta.content = this.toHexColor(raw);
      }
    }
  }

  private toHexColor(cssColor: string): string {
    const ctx = this.document.createElement('canvas').getContext('2d');
    if (!ctx) return cssColor;
    ctx.fillStyle = cssColor;
    ctx.fillRect(0, 0, 1, 1);
    const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
    if (!a) return cssColor;
    return `#${((1 << 24) | (r << 16) | (g << 8) | b).toString(16).slice(1)}`;
  }

  private initialMode(): ThemeMode {
    const saved = this.window?.localStorage.getItem(this.storageKey);
    return saved === 'light' || saved === 'dark' || saved === 'system' ? saved : 'system';
  }

  private watchSystemDark(): boolean {
    const query = this.window?.matchMedia?.('(prefers-color-scheme: dark)');
    query?.addEventListener('change', event => this.isSystemDark.set(event.matches));
    return query?.matches ?? false;
  }
}
