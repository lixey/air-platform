import type { UiIcon } from '../core/generated/contracts';

/** Two chevrons apart: the `chevrons-up-down` and `sort-arrows` icons. */
const CHEVRONS_UP_DOWN = `<svg viewBox="0 0 24 24"><path d="m7 15 5 5 5-5"/><path d="m7 9 5-5 5 5"/></svg>`;

/** Four corners pointing in: the `fullscreen-exit` and `minimize` icons. */
const CORNERS_IN = `<svg viewBox="0 0 32 32"><path d="M4 12 L12 12 12 4 M20 4 L20 12 28 12 M4 20 L12 20 12 28 M28 20 L20 20 20 28"/></svg>`;

/** Four corners pointing out: the `fullscreen` and `maximize` icons. */
const CORNERS_OUT = `<svg viewBox="0 0 32 32"><path d="M4 12 L4 4 12 4 M20 4 L28 4 28 12 M4 20 L4 28 12 28 M28 20 L28 28 20 28"/></svg>`;

/** A globe: the `globe` and `internet` icons. */
const GLOBE = `<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20"/><path d="M2 12h20"/></svg>`;

/** The built-in icons, exactly the names of the server's `UiIcon`: a name missing on either side fails the compile. */
export const ICON_SET = {
  sun: `<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="4"/><path d="M12 2v2"/><path d="M12 20v2"/><path d="m4.93 4.93 1.41 1.41"/><path d="m17.66 17.66 1.41 1.41"/><path d="M2 12h2"/><path d="M20 12h2"/><path d="m6.34 17.66-1.41 1.41"/><path d="m19.07 4.93-1.41 1.41"/></svg>`,
  moon: `<svg viewBox="0 0 24 24"><path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"/></svg>`,
  desktop: `<svg viewBox="0 0 24 24"><rect width="20" height="14" x="2" y="3" rx="2"/><path d="M8 21h8"/><path d="M12 17v4"/></svg>`,
  'sidebar-left': `<svg viewBox="0 0 24 24"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M9 3v18"/><path d="m14 9-3 3 3 3"/></svg>`,
  'sidebar-right': `<svg viewBox="0 0 24 24"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M15 3v18"/><path d="m10 9 3 3-3 3"/></svg>`,
  logout: `<svg viewBox="0 0 24 24"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" x2="9" y1="12" y2="12"/></svg>`,
  users: `<svg viewBox="0 0 24 24"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>`,
  folder: `<svg viewBox="0 0 24 24"><path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/></svg>`,
  gear: `<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>`,
  dots: `<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/></svg>`,
  box: `<svg viewBox="0 0 24 24"><path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/></svg>`,
  apply: `<svg viewBox="0 0 24 24"><path d="M14 4.1 12 6"/><path d="m5.1 8-2.9-.8"/><path d="m6 12-1.9 2"/><path d="M7.2 2.2 8 5.1"/><path d="M9.037 9.69a.498.498 0 0 1 .653-.653l11 4.5a.5.5 0 0 1-.074.949l-4.349 1.041a1 1 0 0 0-.74.739l-1.04 4.35a.5.5 0 0 1-.95.074z"/></svg>`,
  mapPin: `<svg viewBox="0 0 24 24"><path d="M20 10c0 4.993-5.539 10.193-7.399 11.799a1 1 0 0 1-1.202 0C9.539 20.193 4 14.993 4 10a8 8 0 0 1 16 0"/><circle cx="12" cy="10" r="3"/></svg>`,
  'circle-filled': `<svg viewBox="0 0 32 32"><circle cx="16" cy="16" r="10" fill="currentColor"/></svg>`,
  circle: `<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/></svg>`,
  secondary: `<svg viewBox="0 0 32 32"><rect x="2" y="2" width="28" height="28" rx="4" ry="4"/><path d="M17 2 V30"/></svg>`,
  'list-check': `<svg viewBox="0 0 32 32"><path d="M17.333 6.667h10.667"/><path d="M17.333 16h10.667"/><path d="M17.333 25.333h10.667"/><path d="m4 22.667 2.667 2.667 5.333 -5.333"/><path d="m4 9.333 2.667 2.667 5.333 -5.333"/></svg>`,
  secondaryRight: `<svg viewBox="0 0 32 32"><rect x="2" y="2" width="28" height="28" rx="4" ry="4"/><path d="M16 2 V30"/><path d="M6 16 H16"/><path d="M12 12 L16 16 L12 20"/></svg>`,
  secondaryLeft: `<svg viewBox="0 0 32 32"><rect x="2" y="2" width="28" height="28" rx="4" ry="4"/><path d="M16 2 V30"/><path d="M26 16 H16"/><path d="M20 12 L16 16 L20 20"/></svg>`,
  secondaryBottom: `<svg viewBox="0 0 32 32"><rect x="2" y="2" width="28" height="28" rx="4" ry="4"/><path d="M2 16 H30"/><path d="M16 6 V16"/><path d="M12 12 L16 16 L20 12"/></svg>`,
  secondaryTop: `<svg viewBox="0 0 32 32"><rect x="2" y="2" width="28" height="28" rx="4" ry="4"/><path d="M2 16 H30"/><path d="M16 26 V16"/><path d="M12 20 L16 16 L20 20"/></svg>`,
  full: `<svg viewBox="0 0 32 32"><rect x="2" y="2" width="28" height="28" rx="4" ry="4"/></svg>`,
  sidebar: `<svg viewBox="0 0 32 32"><rect x="2" y="2" width="28" height="28" rx="4" ry="4"/><path d="M13 2 V30"/><path stroke-linecap="round" d="M6 7 H9 M6 12 H9 M6 17 H9"/></svg>`,
  rows: `<svg viewBox="0 0 32 32"><path d="M4 4 L28 4 M4 12 L28 12 M4 20 L28 20 M4 28 L28 28"/></svg>`,
  columns: `<svg viewBox="0 0 32 32"><path d="M4 4 L4 28 M12 4 L12 28 M20 4 L20 28 M28 4 L28 28"/></svg>`,
  measure: `<svg viewBox="0 0 32 32"><path d="M24 8 L24 4 L4 4 L14 16 L4 28 L24 28 L24 24"/></svg>`,
  move: `<svg viewBox="0 0 32 32"><path d="M3 16 L29 16 M16 3 L16 29 M12 7 L16 3 20 7 M12 25 L16 29 20 25 M25 12 L29 16 25 20 M7 12 L3 16 7 20"/></svg>`,
  location: `<svg viewBox="0 0 32 32"><circle cx="16" cy="11" r="4"/><path d="M24 15 C21 22 16 30 16 30 16 30 11 22 8 15 5 8 10 2 16 2 22 2 27 8 24 15 Z"/></svg>`,
  'ellipsis-horizontal': `<svg viewBox="0 0 32 32"><circle cx="2" cy="16" r="1" stroke-width="4" /><circle cx="16" cy="16" r="1" stroke-width="4" /><circle cx="30" cy="16" r="1"  stroke-width="4" /></svg>`,
  'ellipsis-vertical': `<svg viewBox="0 0 24 24"><circle cx="11" cy="4" r="1" stroke-width="2"/><circle cx="11" cy="12" r="1" stroke-width="2"/><circle cx="11" cy="20" r="1"  stroke-width="2"/></svg>`,
  'ellipsis-vertical-horizontal': `<svg viewBox="0 0 24 34"><circle cx="12" cy="5" r="1"/><circle cx="19" cy="5" r="1"/><circle cx="5" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/><circle cx="12" cy="19" r="1"/><circle cx="19" cy="19" r="1"/><circle cx="5" cy="19" r="1"/></svg>`,
  telephone: `<svg viewBox="0 0 32 32"><path d="M3 12 C3 5 10 5 16 5 22 5 29 5 29 12 29 20 22 11 22 11 L10 11 C10 11 3 20 3 12 Z M11 14 C11 14 6 19 6 28 L26 28 C26 19 21 14 21 14 L11 14 Z"/><circle cx="16" cy="21" r="4"/></svg>`,
  reload: `<svg viewBox="0 0 32 32"><path d="M29 16 C29 22 24 29 16 29 8 29 3 22 3 16 3 10 8 3 16 3 21 3 25 6 27 9 M20 10 L27 9 28 2"/></svg>`,
  external: `<svg viewBox="0 0 32 32"><path d="M14 9 L3 9 3 29 23 29 23 18 M18 4 L28 4 28 14 M28 4 L14 18"/></svg>`,
  gift: `<svg viewBox="0 0 32 32"><path d="M4 14 L4 30 28 30 28 14 M2 9 L2 14 30 14 30 9 Z M16 9 C 16 9 14 0 8 3 2 6 16 9 16 9 16 9 18 0 24 3 30 6 16 9 16 9 M16 9 L16 30"/></svg>`,
  cart: `<svg viewBox="0 0 32 32"><path d="M6 6 L30 6 27 19 9 19 M27 23 L10 23 5 2 2 2"/><circle cx="25" cy="27" r="2"/><circle cx="12" cy="27" r="2"/></svg>`,
  info: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><path d="M12 17v-6"/><path d="M12 8v-1"/></svg>',
  compose: `<svg viewBox="0 0 32 32"><path d="M27 15 L27 30 2 30 2 5 17 5 M30 6 L26 2 9 19 7 25 13 23 Z M22 6 L26 10 Z M9 19 L13 23 Z"/></svg>`,
  calendar: `<svg viewBox="0 0 24 24"><path d="M8 2v4"/><path d="M16 2v4"/><rect width="18" height="18" x="3" y="4" rx="2"/><path d="M3 10h18"/><path d="M8 14h.01"/><path d="M12 14h.01"/><path d="M16 14h.01"/><path d="M8 18h.01"/><path d="M12 18h.01"/><path d="M16 18h.01"/></svg>`,
  import: `<svg viewBox="0 0 32 32"><path d="M28 22 L28 30 4 30 4 22 M16 4 L16 24 M8 16 L16 24 24 16"/></svg>`,
  export: `<svg viewBox="0 0 32 32"><path d="M28 22 L28 30 4 30 4 22 M16 4 L16 24 M8 12 L16 4 24 12"/></svg>`,
  eye: `<svg viewBox="0 0 32 32"><circle cx="17" cy="15" r="1"/><circle cx="16" cy="16" r="6"/><path d="M2 16 C2 16 7 6 16 6 25 6 30 16 30 16 30 16 25 26 16 26 7 26 2 16 2 16 Z"/></svg>`,
  user: '<svg viewBox="0 0 24 24"><circle cx="12" cy="8" r="5"/><path d="M20 21a8 8 0 0 0-16 0"/></svg>',
  ban: `<svg viewBox="0 0 32 32"><circle cx="16" cy="16" r="14"/><path d="M6 6 L26 26"/></svg>`,
  mail: `<svg viewBox="0 0 32 32"><path d="M2 26 L30 26 30 6 2 6 Z M2 6 L16 16 30 6"/></svg>`,
  photo: `<svg viewBox="0 0 32 32"><path d="M20 24 L12 16 2 26 2 2 30 2 30 24 M16 20 L22 14 30 22 30 30 2 30 2 24"/><circle cx="10" cy="9" r="3"/></svg>`,
  msg: `<svg viewBox="0 0 32 32"><path d="M2 4 L30 4 30 22 16 22 8 29 8 22 2 22 Z"/></svg>`,
  menu: '<svg viewBox="0 0 24 24"><path d="M4 5h16"/><path d="M4 12h16"/><path d="M4 19h16"/></svg>',
  clock: `<svg viewBox="0 0 32 32"><circle cx="16" cy="16" r="14"/><path d="M16 8 L16 16 20 20"/></svg>`,
  heart: `<svg viewBox="0 0 32 32"><path d="M4 16 C1 12 2 6 7 4 12 2 15 6 16 8 17 6 21 2 26 4 31 6 31 12 28 16 25 20 16 28 16 28 16 28 7 20 4 16 Z"/></svg>`,
  options: `<svg viewBox="0 0 32 32"><path d="M28 6 L4 6 M28 16 L4 16 M28 26 L4 26 M24 3 L24 9 M8 13 L8 19 M20 23 L20 29"/></svg>`,
  settings: `<svg viewBox="0 0 32 32"><path d="M13 2 L13 6 11 7 8 4 4 8 7 11 6 13 2 13 2 19 6 19 7 21 4 24 8 28 11 25 13 26 13 30 19 30 19 26 21 25 24 28 28 24 25 21 26 19 30 19 30 13 26 13 25 11 28 8 24 4 21 7 19 6 19 2 Z"/><circle cx="16" cy="16" r="4"/></svg>`,
  'chevrons-up-down': CHEVRONS_UP_DOWN,
  'chevrons-up': `<svg viewBox="0 0 24 24"><path d="m17 11-5-5-5 5"/><path d="m17 18-5-5-5 5"/></svg>`,
  'chevrons-down': `<svg viewBox="0 0 24 24"><path d="m7 6 5 5 5-5"/><path d="m7 13 5 5 5-5"/></svg>`,
  'sort-arrows': CHEVRONS_UP_DOWN,
  'arrow-left': `<svg viewBox="0 0 32 32"><path d="M10 6 L2 16 10 26 M2 16 L30 16"/></svg>`,
  'arrow-bottom': `<svg viewBox="0 0 32 32"><path d="M10 22 L16 30 22 22 M16 30 L16 2"/></svg>`,
  'arrow-top': `<svg viewBox="0 0 32 32"><path d="M10 10 L16 2 22 10 M16 2 L16 30"/></svg>`,
  'arrow-right': `<svg viewBox="0 0 32 32"><path d="M22 6 L30 16 22 26 M30 16 L2 16"/></svg>`,
  checkmark: `<svg viewBox="0 0 24 24"><path d="M20 6 9 17l-5-5"/></svg>`,
  star: `<svg viewBox="0 0 32 32"><path d="M16 2 L20 12 30 12 22 19 25 30 16 23 7 30 10 19 2 12 12 12 Z"/></svg>`,
  'fullscreen-exit': CORNERS_IN,
  fullscreen: CORNERS_OUT,
  maximize: CORNERS_OUT,
  minimize: CORNERS_IN,
  minus: `<svg viewBox="0 0 32 32"><path d="M2 16 L30 16"/></svg>`,
  alert: `<svg viewBox="0 0 32 32"><path d="M16 3 L30 29 2 29 Z M16 11 L16 19 M16 23 L16 25"/></svg>`,
  'chevron-top': `<svg viewBox="0 0 24 24"><path d="m18 15-6-6-6 6"/></svg>`,
  'chevron-right': `<svg viewBox="0 0 24 24"><path d="m9 18 6-6-6-6"/></svg>`,
  'chevron-bottom': `<svg viewBox="0 0 24 24"><path d="m6 9 6 6 6-6"/></svg>`,
  'chevron-left': `<svg viewBox="0 0 24 24"><path d="m15 18-6-6 6-6"/></svg>`,
  close: `<svg viewBox="0 0 24 24"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>`,
  download: `<svg viewBox="0 0 32 32"><path d="M9 22 C0 23 1 12 9 13 6 2 23 2 22 10 32 7 32 23 23 22 M11 26 L16 30 21 26 M16 16 L16 30" /></svg>`,
  excel: `<svg viewBox="0 0 32 32"><path d="M6 2 L6 30 26 30 26 10 18 2 Z M18 2 L18 10 26 10 M12 14 20 24 M20 14 12 24" /></svg>`,
  filter: `<svg viewBox="0 0 24 24"><path d="M10 20a1 1 0 0 0 .553.895l2 1A1 1 0 0 0 14 21v-7a2 2 0 0 1 .517-1.341L21.74 4.67A1 1 0 0 0 21 3H3a1 1 0 0 0-.742 1.67l7.225 7.989A2 2 0 0 1 10 14z"/></svg>`,
  home: `<svg viewBox="0 0 32 32"><path d="M12 20 L12 30 4 30 4 12 16 2 28 12 28 30 20 30 20 20 Z" /></svg>`,
  plus: `<svg viewBox="0 0 24 24"><path d="M5 12h14"/><path d="M12 5v14"/></svg>`,
  play: `<svg viewBox="0 0 24 24"><path d="M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z"/></svg>`,
  print: `<svg viewBox="0 0 24 24"><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><path d="M6 9V3a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v6"/><rect x="6" y="14" width="12" height="8" rx="1"/></svg>`,
  globe: GLOBE,
  'grip-vertical': `<svg viewBox="0 0 24 24"><circle cx="9" cy="12" r="1"/><circle cx="9" cy="5" r="1"/><circle cx="9" cy="19" r="1"/><circle cx="15" cy="12" r="1"/><circle cx="15" cy="5" r="1"/><circle cx="15" cy="19" r="1"/></svg>`,
  phone: `<svg viewBox="0 0 24 24"><path d="M13.832 16.568a1 1 0 0 0 1.213-.303l.355-.465A2 2 0 0 1 17 15h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2A18 18 0 0 1 2 4a2 2 0 0 1 2-2h3a2 2 0 0 1 2 2v3a2 2 0 0 1-.8 1.6l-.468.351a1 1 0 0 0-.292 1.233 14 14 0 0 0 6.392 6.384"/></svg>`,
  search: '<svg viewBox="0 0 24 24"><path d="m21 21-4.34-4.34"/><circle cx="11" cy="11" r="8"/></svg>',
  save: `<svg viewBox="0 0 24 24"><path d="M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path d="M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7"/><path d="M7 3v4a1 1 0 0 0 1 1h7"/></svg>`,
  signout:
    '<svg viewBox="0 0 24 24"><path d="m16 17 5-5-5-5"/><path d="M21 12H9"/><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/></svg>',
  signin:
    '<svg viewBox="0 0 24 24"><path d="m10 17 5-5-5-5"/><path d="M15 12H3"/><path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/></svg>',
  trash: `<svg viewBox="0 0 24 24"><path d="M10 11v6"/><path d="M14 11v6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/><path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>`,
  multiple: `<svg viewBox="0 0 24 24"><path d="m12 15 2 2 4-4"/><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>`,
  map: '<svg viewBox="0 0 24 24"><path d="M14.106 5.553a2 2 0 0 0 1.788 0l3.659-1.83A1 1 0 0 1 21 4.619v12.764a1 1 0 0 1-.553.894l-4.553 2.277a2 2 0 0 1-1.788 0l-4.212-2.106a2 2 0 0 0-1.788 0l-3.659 1.83A1 1 0 0 1 3 19.381V6.618a1 1 0 0 1 .553-.894l4.553-2.277a2 2 0 0 1 1.788 0z"/><path d="M15 5.764v15"/><path d="M9 3.236v15"/></svg>',
  internet: GLOBE,
  question:
    '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/><path d="M12 17h.01"/></svg>',
  edit: '<svg viewBox="0 0 24 24"><path d="M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.375 2.625a1 1 0 0 1 3 3l-9.013 9.014a2 2 0 0 1-.853.505l-2.873.84a.5.5 0 0 1-.62-.62l.84-2.873a2 2 0 0 1 .506-.852z"/></svg>',
  spinner:
    '<svg viewBox="0 0 24 24" class="animate-spin"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" fill="none"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg>',
  chart:
    '<svg viewBox="0 0 24 24"><path d="M3 3v16a2 2 0 0 0 2 2h16"/><rect x="15" y="5" width="4" height="12" rx="1"/><rect x="7" y="8" width="4" height="9" rx="1"/></svg>',
  kanban: '<svg viewBox="0 0 24 24"><path d="M6 5v11"/><path d="M12 5v6"/><path d="M18 5v14"/></svg>',
  panelRightClose:
    '<svg viewBox="0 0 24 24"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M15 3v18"/><path d="m8 9 3 3-3 3"/></svg>',
  panelRightOpen:
    '<svg viewBox="0 0 24 24"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M15 3v18"/><path d="m10 15-3-3 3-3"/></svg>',
  panelBottomOpen:
    '<svg viewBox="0 0 24 24"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M3 15h18"/><path d="m9 10 3-3 3 3"/></svg>',
  panelBottomClose:
    '<svg viewBox="0 0 24 24"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M3 15h18"/><path d="m15 8-3 3-3-3"/></svg>',
  pin: `<svg viewBox="0 0 24 24"><path d="M12 17v5"/><path d="M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z"/></svg>`,
  shelving: `<svg viewBox="0 0 24 24"><path d="M12 12V9a1 1 0 0 0-1-1H9a1 1 0 0 0-1 1v3"/><path d="M16 20v-3a1 1 0 0 0-1-1h-2a1 1 0 0 0-1 1v3"/><path d="M20 22V2"/><path d="M4 12h16"/><path d="M4 20h16"/><path d="M4 2v20"/><path d="M4 4h16"/></svg>`,
} satisfies Record<UiIcon, string>;
