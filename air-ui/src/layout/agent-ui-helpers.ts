const MAX_PROMPT_HEIGHT = 160;

export function fitPromptToText(area: HTMLTextAreaElement): void {
  if (!area.value) {
    area.style.height = '2.25rem';
    area.style.overflowY = 'hidden';
    return;
  }
  area.style.height = 'auto';
  const full = area.scrollHeight + area.offsetHeight - area.clientHeight;
  const clamped = Math.min(MAX_PROMPT_HEIGHT, Math.max(36, full));
  area.style.height = `${clamped}px`;
  area.style.overflowY = full > MAX_PROMPT_HEIGHT ? 'auto' : 'hidden';
}

export function formatShortAgentDateTime(date: Date): string {
  const d = date instanceof Date ? date : new Date(date);
  const now = new Date();
  const sameDay = d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate();
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  if (sameDay) {
    return `${hh}:${mm}`;
  }
  const day = String(d.getDate()).padStart(2, '0');
  const mon = String(d.getMonth() + 1).padStart(2, '0');
  return `${day}.${mon} ${hh}:${mm}`;
}

export function formatFullAgentDateTime(date: Date): string {
  const d = date instanceof Date ? date : new Date(date);
  const day = String(d.getDate()).padStart(2, '0');
  const mon = String(d.getMonth() + 1).padStart(2, '0');
  const yr = d.getFullYear();
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  const ss = String(d.getSeconds()).padStart(2, '0');
  return `${day}.${mon}.${yr} ${hh}:${mm}:${ss}`;
}
