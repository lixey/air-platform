/** The frame where animation frames do not come: a tab out of view still lays itself out. */
const FALLBACK_FRAME_MS = 50;

/**
 * One pending frame at a time: the next animation frame, or a short timer where frames do not come. The frame runs
 * once, by whichever of the two comes first, and the other is cancelled; a schedule while one is pending does nothing,
 * and without `requestAnimationFrame` the timer alone runs it.
 */
export class AnimationFrame {
  /** The animation frame's id; -1 stands for a frame that only the timer runs, null for none pending. */
  private frameId: number | null = null;
  private frameTimer: ReturnType<typeof setTimeout> | null = null;

  schedule(run: () => void): void {
    if (this.frameId !== null) {
      return;
    }

    const once = (): void => {
      this.cancel();
      run();
    };

    this.frameId = typeof requestAnimationFrame === 'undefined' ? -1 : requestAnimationFrame(once);
    this.frameTimer = setTimeout(once, FALLBACK_FRAME_MS);
  }

  /** Drops the pending frame, both its animation frame and its timer. */
  cancel(): void {
    const frameId = this.frameId;
    const hasAnimationFrame = frameId !== null && frameId >= 0 && typeof cancelAnimationFrame !== 'undefined';
    if (hasAnimationFrame) {
      cancelAnimationFrame(frameId);
    }
    this.frameId = null;
    if (this.frameTimer !== null) {
      clearTimeout(this.frameTimer);
      this.frameTimer = null;
    }
  }
}
