/**
 * The browser's own warning before the page unloads. The switch it hands back turns the warning on while there is
 * something to protect and off once there is not; a repeated setting changes nothing. The browser shows its own text
 * and cannot wait for a save.
 */
export function unloadWarning(): (protect: boolean) => void {
  let isOn = false;
  const warn = (event: BeforeUnloadEvent) => {
    event.preventDefault();
    // Browsers before the standard `preventDefault` warn only for a set return value
    event.returnValue = '';
  };
  return protect => {
    if (protect === isOn) {
      return;
    }
    isOn = protect;
    if (protect) {
      window.addEventListener('beforeunload', warn);
    } else {
      window.removeEventListener('beforeunload', warn);
    }
  };
}
