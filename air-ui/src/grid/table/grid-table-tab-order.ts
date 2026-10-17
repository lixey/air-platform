const CONTROLS = ['a[href]', 'button', 'input', 'select', 'textarea', '[tabindex]:not([tabindex="-1"])'];

/** What a cell may hold that would take a Tab stop of its own: the table is one stop, its cells are reached by the arrows. */
const CELL_CONTROLS = CONTROLS.join(', ');

/** Such a control under a body or a foot, as the table finds it; the header's own controls keep their Tab stops. */
const CELL_TAB_STOPS = CONTROLS.flatMap(control => [`tbody ${control}`, `tfoot ${control}`]).join(', ');

/**
 * A row the table draws to say how its page stands — failed or empty — rather than a row of the result: no active cell
 * reaches it, so its controls, the failed page's retry, keep their Tab stops and answer their own keys.
 */
export const STATUS_ROW = 'tr[data-grid-state]';

/**
 * Keeps the table the one Tab stop: every control a body or pinned cell draws — at once, later, or whose `tabindex` a
 * binding sets again — is taken out of the Tab order and reached through the active cell; a status row's controls stay
 * in it, and when such a row goes away with the focus — the retry's page arrived — the table takes the focus back.
 * Returns what stops watching.
 */
export function keepCellControlsOutOfTabOrder(table: HTMLTableElement): () => void {
  leaveTabOrder(table);
  // The last control inside the table that took the focus: gone with its status row, it hands the focus back
  let focused: Element | null = null;
  const onFocusIn = (event: FocusEvent) => (focused = event.target instanceof Element ? event.target : null);
  table.addEventListener('focusin', onFocusIn);
  const observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === 'attributes') {
        leaveTabOrder(record.target);
      } else {
        record.addedNodes.forEach(leaveTabOrder);
      }
    }
    if (wentWithStatusRow(focused)) {
      focused = null;
      table.focus({ preventScroll: true });
    }
  });
  observer.observe(table, { childList: true, subtree: true, attributes: true, attributeFilter: ['tabindex'] });
  return () => {
    observer.disconnect();
    table.removeEventListener('focusin', onFocusIn);
  };
}

/** Takes the cell controls under `node`, and `node` itself, out of the Tab order. */
function leaveTabOrder(node: Node): void {
  if (!(node instanceof Element)) {
    return;
  }
  // Inside a body or a foot every control is a cell's; above them, the table's, only those under one are
  const controls = node.closest('tbody, tfoot') ? [node, ...node.querySelectorAll(CELL_CONTROLS)] : node.querySelectorAll(CELL_TAB_STOPS);
  for (const control of controls) {
    // Written only when it differs: the write is itself a change of `tabindex` the observer is told of
    if (isCellControl(control) && control.getAttribute('tabindex') !== '-1') {
      control.setAttribute('tabindex', '-1');
    }
  }
}

/** A control of a row's cell: one the table takes out of the Tab order, unlike a status row's. */
function isCellControl(element: Element): boolean {
  return element.matches(CELL_CONTROLS) && element.closest(STATUS_ROW) === null;
}

/** Whether `focused` left the page with the status row it stood in, and the focus fell to the page's body with it. */
function wentWithStatusRow(focused: Element | null): boolean {
  if (focused === null || focused.isConnected) {
    return false;
  }
  const document = focused.ownerDocument;
  return focused.closest(STATUS_ROW) !== null && document.activeElement === document.body;
}
