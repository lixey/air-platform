import { signal } from '@angular/core';
import type { GridRowKey } from '../grid/grid-row';
import type { FileView } from './file-view';

/**
 * The links of the file viewer that failed to load and what a fresh one continues: a failed link is not tried
 * again, the first failure of a file asks the opener's page again, and a video plays on from the moment its failed
 * link stopped at. Created in a field of the viewer; it is given how the page is asked again.
 */
export class FileViewerLinks {
  /** The links that failed to load; a fresh link of the same file is tried again. */
  private readonly failedLinks = signal<ReadonlySet<string>>(new Set());

  /** The files whose failed image or video has asked the opener's page again, each once. */
  private readonly refreshedKeys = new Set<GridRowKey>();

  /** The moments, in seconds, the videos that failed stopped at, each played on from there by its fresh link. */
  private readonly resumeTimes = signal<ReadonlyMap<GridRowKey, number>>(new Map());

  constructor(private readonly refresh: () => void) {}

  /** Whether the link failed to load here. */
  hasFailed(link: string): boolean {
    return this.failedLinks().has(link);
  }

  /** Where the video of the file plays on from by its fresh link, in seconds; null where it starts afresh. */
  resumesAt(key: GridRowKey | null | undefined): number | null {
    return key == null ? null : (this.resumeTimes().get(key) ?? null);
  }

  /**
   * An image or a video that failed to load: the first failure of a file asks the opener's page again for fresh links;
   * the link that failed is not tried again, so a fresh one shows and a stale one leaves the card with the reason.
   */
  imageFailed(file: FileView): void {
    const url = file.url;
    if (url) {
      this.failedLinks.update(links => new Set(links).add(url));
    }
    if (file.key !== null && !this.refreshedKeys.has(file.key)) {
      this.refreshedKeys.add(file.key);
      this.refresh();
    }
  }

  /**
   * A video that failed to load or to play on, its link expired among the reasons: the first failure of a file keeps
   * the moment it stopped at, to play on from there by the fresh link; a second one leaves the card with Download.
   */
  videoFailed(file: FileView, event: Event): void {
    const key = file.key;
    if (key !== null && !this.refreshedKeys.has(key)) {
      const at = event.target instanceof HTMLMediaElement ? event.target.currentTime : 0;
      this.resumeTimes.update(times => new Map(times).set(key, at));
    } else if (key !== null) {
      this.forgetResume(key);
    }
    this.imageFailed(file);
  }

  /**
   * A video's fresh link has loaded its metadata: it seeks to the moment the failed link stopped at, and plays on from
   * there by `autoplay` where the browser lets it, or once the user plays it.
   */
  videoLoaded(file: FileView, event: Event): void {
    const at = file.key === null ? undefined : this.resumeTimes().get(file.key);
    if (at !== undefined && event.target instanceof HTMLMediaElement) {
      event.target.currentTime = at;
    }
  }

  /** A video plays: the moment it played on from is spent, and its player starts afresh when shown again. */
  videoPlaying(file: FileView): void {
    if (file.key !== null) {
      this.forgetResume(file.key);
    }
  }

  thumbFailed(picture: string): void {
    this.failedLinks.update(links => new Set(links).add(picture));
  }

  private forgetResume(key: GridRowKey): void {
    if (this.resumeTimes().has(key)) {
      this.resumeTimes.update(times => {
        const rest = new Map(times);
        rest.delete(key);
        return rest;
      });
    }
  }
}
