import { Service, signal } from '@angular/core';
import texts from './generated/ui-texts.json';

export type UiTextKey = keyof typeof texts;

/** The platform's texts: the metadata's, in the request's culture, or the generated English default before it arrives. */
@Service()
export class TextsService {
  private readonly _texts = signal<Readonly<Record<string, string>>>({});
  readonly texts = this._texts.asReadonly();

  of(key: UiTextKey, ...args: readonly (string | number)[]): string {
    const text = this.texts()[key] ?? texts[key];
    return args.length ? text.replace(/\{(\d+)\}/g, (_, i) => String(args[Number(i)] ?? '')) : text;
  }

  /** The metadata's texts, which replace the ones held; an empty record falls back to the defaults. */
  setTexts(texts: Readonly<Record<string, string>>): void {
    this._texts.set(texts);
  }
}
