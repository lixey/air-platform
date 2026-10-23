import type { OverlayModelDto, OverlayPresentation } from '../core/generated/contracts';
import { parseSize, toBound } from '../panels/panel-size';

export interface OverlayRect {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** A card's measured extent in pixels: its border box. */
export interface OverlaySize {
  width: number;
  height: number;
}

/** Where the box sits inside its layer, on each axis. */
export type OverlayAlign = 'start' | 'center' | 'end';

/**
 * The distances the host resolves overlays with, in pixels: the panel gap, the margin every
 * overlay keeps from its frame's edges, and the offset a menu's corner takes from its
 * trigger's corner. The last two are spacing steps in rem, read from the root font size.
 */
export interface OverlayUnits {
  gap: number;
  margin: number;
  offset: number;
}

/** The size a screen asks of the dialog that shows it: panel sizes, as the model's own. */
export interface OverlayAsk {
  width?: string | null;
  height?: string | null;
}

/**
 * What an overlay is placed against: the work area, the element it hangs from, the browser window, the host's
 * distances, and the size its card was last measured at. No anchor and no size are the same as `null`: nothing to
 * hang from, nothing measured yet. `keepsOwnWidth` is the opener's word that a menu or a popover drops the floor of
 * its anchor's width, `hangsDownOnly` its word that the card hangs under its anchor or is a dialog.
 */
export interface OverlayMeasures {
  work: OverlayRect;
  anchor?: OverlayRect | null;
  viewport: OverlayRect;
  units: OverlayUnits;
  size?: OverlaySize | null;
  keepsOwnWidth?: boolean;
  hangsDownOnly?: boolean;
  /** What the overlay's screen asked with its last answer; read for a dialog whose model states no size. */
  asked?: OverlayAsk | null;
}

/**
 * Where an overlay stands and how big it is.
 *
 * `layer` is the frame the box is laid out in — a fixed region of the window — `align`
 * where it sits inside it on each axis, and `offset` how far it is then moved from the
 * edge it is aligned to, in whole pixels: a menu's corner meets its trigger's this way.
 * The box is the overlay's one card, and its sizes are the card's. `null` for a size means
 * as big as the content; the maximums are the frame's extents, but the greatest height of a
 * card that hangs down only and hangs, which is the room between its top edge and the frame's
 * bottom edge.
 */
export interface OverlayBox {
  presentation: OverlayPresentation;
  layer: OverlayRect;
  align: { x: OverlayAlign; y: OverlayAlign };
  offset: { x: number; y: number };
  width: number | null;
  height: number | null;
  minWidth: number;
  maxWidth: number;
  maxHeight: number;
  /** The window is narrow: the overlay fills it edge to edge, with no radius or margin. */
  isNarrow: boolean;
}

/** Under this window width, every overlay fills the window. */
export const OVERLAY_NARROW_WIDTH = 640;

/** A dialog centres in the window, or in this much of it from the left when the window is wider. */
export const DIALOG_FRAME = 3840;

/**
 * Resolves an overlay's box from its model and what it is placed against: the work area, its
 * anchor, the browser window and the size its card was last measured at.
 *
 * Every overlay stands in a frame, a margin in from every edge: a drawer in the work area,
 * a dialog in the window's frame — the window, or its leftmost 3840 pixels — and a menu or
 * a popover in the window. A dialog is centred; a drawer stands in the frame's corner at
 * its full height; a menu hangs from its trigger by the size it wants — its model's pixels,
 * else the size measured — on each axis apart, and an anchor out of sight makes it a
 * dialog. Widths and heights are panel sizes measured against the frame the overlay
 * stands in, clamped to it and to the model's `maxWidth`: the library holds no cap of its own.
 * A dialog whose model states neither size takes the one its screen asked.
 *
 * A menu or a popover opened to hang down only hangs where the height its model states fits
 * under its anchor's corner, and is otherwise a dialog as tall as its frame, as it is with its
 * anchor out of sight: the choice reads the stated height, the anchor and the frame, never the
 * measured size, so it does not change while the content grows and shrinks.
 */
export function resolveOverlay(
  model: OverlayModelDto,
  { work, anchor, viewport, units, size, keepsOwnWidth, hangsDownOnly, asked }: OverlayMeasures,
): OverlayBox {
  const isOutOfSight = !anchor || !overlaps(anchor, viewport);
  const anchored: OverlayPresentation = isAnchored(model.presentation) && isOutOfSight ? 'dialog' : model.presentation;

  if (viewport.width < OVERLAY_NARROW_WIDTH || (model.key === 'layout.agent' && viewport.width < 1024)) {
    return fill(anchored, viewport);
  }

  // The word is a menu's and a popover's: on a dialog and a drawer it says nothing
  const hangsOnly = (hangsDownOnly ?? false) && isAnchored(model.presentation);
  const windowFrame = shrink(viewport, units.margin);
  const presentation: OverlayPresentation =
    hangsOnly && isAnchored(anchored) && anchor && !fitsBelow(model, anchor, windowFrame, units.offset) ? 'dialog' : anchored;

  if (isAnchored(presentation) && anchor) {
    return menuBox(model, anchor, windowFrame, units.offset, size ?? null, presentation, keepsOwnWidth ?? false, hangsOnly, asked);
  }

  if (presentation === 'drawer') {
    const frame = shrink(work, units.margin);
    const cap = toBound(model.maxWidth ?? undefined);
    const maxWidth = cap !== undefined ? Math.min(cap, frame.width) : frame.width;
    const widthModel = model.width ?? '1fr';

    return {
      presentation,
      layer: frame,
      align: { x: 'start', y: 'start' },
      offset: { x: 0, y: 0 },
      width: sized(widthModel, frame.width, maxWidth),
      height: frame.height,
      minWidth: 0,
      maxWidth,
      maxHeight: frame.height,
      isNarrow: false,
    };
  }

  const cap = toBound(model.maxWidth ?? undefined);
  const frame = shrink({ ...viewport, width: Math.min(viewport.width, DIALOG_FRAME) }, units.margin);
  const maxWidth = cap !== undefined ? Math.min(cap, frame.width) : frame.width;
  // The model's presentation, not the resolved one: a popover that presents as a dialog keeps its own sizes
  const follows = (model.presentation === 'dialog' || model.presentation === 'popover') && model.width == null && model.height == null;
  const width = follows ? asked?.width : model.width;
  const height = follows ? asked?.height : model.height;

  return {
    presentation,
    layer: frame,
    align: { x: 'center', y: 'center' },
    offset: { x: 0, y: 0 },
    width: sized(width, frame.width, maxWidth),
    // A card that hangs down only has no field to stand beside here: it takes the frame, whatever its content takes
    height: hangsOnly ? frame.height : sized(height, frame.height, frame.height),
    minWidth: 0,
    maxWidth,
    maxHeight: frame.height,
    isNarrow: false,
  };
}

/**
 * Whether the height a model states fits between the near corner of its anchor on the vertical axis, `inward` under
 * the anchor's top and inside the frame as `place` takes it, and the frame's bottom edge. A model that states no height
 * in pixels fits nowhere. The measured size is not read.
 */
function fitsBelow(model: OverlayModelDto, anchor: OverlayRect, frame: OverlayRect, inward: number): boolean {
  const stated = toBound(model.height ?? undefined);
  const frameEnd = frame.top + frame.height;
  return stated !== undefined && stated <= frameEnd - clamp(anchor.top + inward, frame.top, frameEnd);
}

/**
 * A menu covers its trigger: its card's top-left corner on the trigger's, shifted `inward`
 * right and down, so the card starts inside the button and hangs from it — when the size
 * it wants fits there within the frame. The wanted size is the model's `width` and `height`
 * in pixels, else the size the card was measured at, else nothing, which fits anywhere. On
 * each axis apart, a card that does not fit near turns to the far corner when it fits there,
 * slides over its trigger to the frame's edge when it fits on neither side, and takes the
 * frame's extent when it exceeds it. The room beside the anchor decides where the card
 * stands, never how big it is: the maximums are the frame's.
 *
 * A stated width is the card's, at least the anchor's unless the card keeps its own width,
 * and within the frame. A stated height places the card — a select's menu states the
 * height its full list will take, so that it stands where that fits before the list
 * arrives — and is the card's own where the card stands above its anchor or is slid over
 * it: there the card is pinned by its bottom edge, and a height that followed its content
 * would move its top edge with every row. Hanging below its anchor, or clamped to the
 * frame, the card stays as tall as its content.
 *
 * A card that hangs down only stands here where its stated height, the least room it needs,
 * fits below its anchor: as tall as its content, and at most the room between its top edge
 * and the frame's bottom edge, which reads the anchor and the frame and never the measured size.
 */
function menuBox(
  model: OverlayModelDto,
  anchor: OverlayRect,
  frame: OverlayRect,
  inward: number,
  size: OverlaySize | null,
  presentation: OverlayPresentation,
  keepsOwnWidth: boolean,
  hangsDownOnly: boolean,
  asked?: OverlayAsk | null,
): OverlayBox {
  const wantedWidth = toBound(asked?.width ?? model.width ?? undefined);
  const wantedHeight = toBound(asked?.height ?? model.height ?? undefined);
  const x = place(wantedWidth ?? size?.width ?? 0, frame.left, frame.left + frame.width, anchor.left, anchor.left + anchor.width, inward);
  const y = place(wantedHeight ?? size?.height ?? 0, frame.top, frame.top + frame.height, anchor.top, anchor.top + anchor.height, inward);
  const minWidth = keepsOwnWidth ? 0 : Math.min(anchor.width, frame.width);

  return {
    presentation,
    layer: frame,
    align: { x: x.align, y: y.align },
    offset: { x: x.offset, y: y.offset },
    width: wantedWidth === undefined ? null : Math.min(Math.max(wantedWidth, minWidth), frame.width),
    height: wantedHeight !== undefined && y.align === 'end' ? wantedHeight : null,
    minWidth,
    maxWidth: frame.width,
    maxHeight: hangsDownOnly ? frame.height - y.offset : frame.height,
    isNarrow: false,
  };
}

/** One axis of a menu's placement: the frame's edge the card is aligned to, and its offset from it. */
interface Placement {
  align: OverlayAlign;
  offset: number;
}

/**
 * Places a menu on one axis by the extent it wants, between the frame's edges: near — from
 * the anchor's near corner, `inward` into it, towards the frame's far edge; far — back from
 * the anchor's far corner towards the frame's near edge; slid — its far edge on the frame's,
 * over the anchor; clamped — the frame itself. A corner beyond the frame is taken at the
 * frame's edge, so the card never stands outside it. Offsets are whole pixels.
 */
function place(wanted: number, frameStart: number, frameEnd: number, anchorStart: number, anchorEnd: number, inward: number): Placement {
  const near = clamp(anchorStart + inward, frameStart, frameEnd);
  const far = clamp(anchorEnd - inward, frameStart, frameEnd);

  if (wanted <= frameEnd - near) {
    return { align: 'start', offset: whole(near - frameStart) };
  }

  if (wanted <= far - frameStart) {
    return { align: 'end', offset: whole(far - frameEnd) };
  }

  return { align: wanted <= frameEnd - frameStart ? 'end' : 'start', offset: 0 };
}

/** Whether a presentation hangs from an anchor: a menu and a popover do, while their anchor is in sight. */
function isAnchored(presentation: OverlayPresentation): boolean {
  return presentation === 'menu' || presentation === 'popover';
}

/** On a narrow window every presentation is the window itself, edge to edge. */
function fill(presentation: OverlayPresentation, viewport: OverlayRect): OverlayBox {
  return {
    presentation,
    layer: viewport,
    align: { x: 'start', y: 'start' },
    offset: { x: 0, y: 0 },
    width: viewport.width,
    height: viewport.height,
    minWidth: 0,
    maxWidth: viewport.width,
    maxHeight: viewport.height,
    isNarrow: true,
  };
}

/** A size measured against a frame, capped. */
function sized(size: string | null | undefined, extent: number, max: number): number | null {
  const { value, unit } = parseSize(size ?? 'auto');

  switch (unit) {
    case 'auto':
      return null;
    case 'px':
      return Math.min(value, max);
    case '%':
      return Math.min((extent * value) / 100, max);
    case 'fr':
      return max;
  }
}

function shrink(rect: OverlayRect, by: number): OverlayRect {
  return {
    left: rect.left + by,
    top: rect.top + by,
    width: Math.max(0, rect.width - 2 * by),
    height: Math.max(0, rect.height - 2 * by),
  };
}

function overlaps(a: OverlayRect, b: OverlayRect): boolean {
  return a.left < b.left + b.width && a.left + a.width > b.left && a.top < b.top + b.height && a.top + a.height > b.top;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

/** Rounded to a whole pixel, with no negative zero to write as `-0px`. */
function whole(value: number): number {
  return Math.round(value) || 0;
}
