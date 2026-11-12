import type { ReportColumnDto, ReportTableDto, RowDto } from '../core/generated/contracts';

function stripCellFormatting(cell: string): string {
  return cell
    .trim()
    .replace(/^\*\*(.+)\*\*$/, '$1')
    .replace(/^__(.+)__$/, '$1')
    .replace(/^`(.+)`$/, '$1')
    .trim();
}

function splitRawTableRow(line: string): string[] {
  const trimmed = line.trim().replace(/^\|/, '').replace(/\|$/, '');
  return trimmed.split('|').map(c => c.trim());
}

function splitMarkdownTableRow(line: string): string[] {
  return splitRawTableRow(line).map(stripCellFormatting);
}

function isSeparatorRow(line: string): boolean {
  const trimmed = line.trim();
  if (!trimmed.includes('|') || !trimmed.includes('-')) {
    return false;
  }
  const cells = splitMarkdownTableRow(trimmed);
  return cells.length >= 2 && cells.every(c => /^:?-{2,}:?$/.test(c));
}

function parseNumericCell(val: string): { num: number; isPercent: boolean } | null {
  if (!val) {
    return null;
  }
  const clean = val
    .replace(/\*\*/g, '')
    .replace(/\p{Sc}/gu, '')
    .replace(/^(?:[A-Z]{3})\s+(?=[+-]?\d)|(?<=\d)\s+(?:[A-Z]{3})$/g, '')
    .trim();
  if (!clean || /\p{L}/u.test(clean)) {
    return null;
  }
  const isPercent = clean.endsWith('%');
  const rawNum = (isPercent ? clean.slice(0, -1) : clean).replace(/[\s\u00A0\u202F']/g, '');
  const normalized =
    rawNum.includes(',') && rawNum.includes('.')
      ? rawNum.lastIndexOf(',') > rawNum.lastIndexOf('.')
        ? rawNum.replace(/\./g, '').replace(',', '.')
        : rawNum.replace(/,/g, '')
      : rawNum.replace(',', '.');
  if (!/^[+-]?\d+(?:\.\d+)?$/.test(normalized)) {
    return null;
  }
  const num = Number(normalized);
  return Number.isFinite(num) ? { num, isPercent } : null;
}

function isBoldSummaryRow(rawRowLine: string): boolean {
  const rawCells = splitRawTableRow(rawRowLine);
  const first = rawCells[0] ?? '';
  return /^\*\*.+\*\*$/.test(first) || /^__.+__$/.test(first);
}

/**
 * Splits a markdown chunk into alternating markdown segments and parsed `ReportTableDto` objects whenever
 * a GitHub-Flavored Markdown (GFM) pipe table is encountered.
 */
export function splitMarkdownAndGfmTables(
  markdown: string,
): readonly ({ kind: 'markdown'; content: string } | { kind: 'table'; table: ReportTableDto })[] {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  const result: ({ kind: 'markdown'; content: string } | { kind: 'table'; table: ReportTableDto })[] = [];
  let mdBuffer: string[] = [];
  let activeFence: string | null = null;
  let i = 0;

  const flushMd = (): void => {
    const text = mdBuffer.join('\n').trim();
    if (text) {
      result.push({ kind: 'markdown', content: text });
    }
    mdBuffer = [];
  };

  while (i < lines.length) {
    const line = lines[i];
    const fenceMatch = /^ {0,3}(`{3,}|~{3,})/.exec(line);
    if (fenceMatch) {
      const marker = fenceMatch[1];
      if (!activeFence) {
        activeFence = marker;
      } else if (marker[0] === activeFence[0] && marker.length >= activeFence.length && /^ {0,3}(`{3,}|~{3,})\s*$/.test(line)) {
        activeFence = null;
      }
      mdBuffer.push(line);
      i++;
      continue;
    }

    if (activeFence) {
      mdBuffer.push(line);
      i++;
      continue;
    }

    const nextLine = i + 1 < lines.length ? lines[i + 1] : '';

    if (line.trim().includes('|') && isSeparatorRow(nextLine)) {
      const headers = splitMarkdownTableRow(line);
      const alignments = splitMarkdownTableRow(nextLine).map(sep => (sep.endsWith(':') && !sep.startsWith(':') ? 'right' : 'left'));
      const rawDataRows: string[][] = [];
      let lastRawLine = '';
      i += 2;

      while (i < lines.length && lines[i].trim().includes('|') && !/^ {0,3}(`{3,}|~{3,})/.test(lines[i])) {
        if (!isSeparatorRow(lines[i])) {
          lastRawLine = lines[i];
          rawDataRows.push(splitMarkdownTableRow(lines[i]));
        }
        i++;
      }

      if (headers.length >= 2 && rawDataRows.length >= 1) {
        flushMd();

        const columns: ReportColumnDto[] = headers.map((h, colIdx) => {
          let numericCount = 0;
          let percentCount = 0;
          let nonEmptyCount = 0;

          for (const r of rawDataRows) {
            const cellVal = r[colIdx] ?? '';
            if (!cellVal || cellVal === '-' || cellVal === '—') {
              continue;
            }
            nonEmptyCount++;
            const parsed = parseNumericCell(cellVal);
            if (parsed) {
              numericCount++;
              if (parsed.isPercent) {
                percentCount++;
              }
            }
          }

          const isMostlyNumeric = colIdx > 0 && nonEmptyCount > 0 && numericCount / nonEmptyCount >= 0.6;
          const isPercentCol = isMostlyNumeric && percentCount / numericCount >= 0.5;
          const kind: ReportColumnDto['kind'] = isPercentCol
            ? 'percent'
            : isMostlyNumeric || alignments[colIdx] === 'right'
              ? 'number'
              : 'text';

          return {
            key: `c${colIdx}`,
            label: h,
            kind,
          };
        });

        const mapCellsToRow = (cells: string[]): RowDto => {
          const rowObj: Record<string, unknown> = {};
          columns.forEach((col, colIdx) => {
            const rawCell = cells[colIdx] ?? '';
            if (col.kind === 'number' || col.kind === 'money' || col.kind === 'percent') {
              const parsed = parseNumericCell(rawCell);
              rowObj[col.key] = parsed ? parsed.num : rawCell;
            } else {
              rowObj[col.key] = rawCell;
            }
          });
          return { cells: rowObj };
        };

        let bodyCells = rawDataRows;
        let bottomRows: RowDto[] | null = null;
        if (rawDataRows.length > 1 && isBoldSummaryRow(lastRawLine)) {
          bodyCells = rawDataRows.slice(0, -1);
          bottomRows = [mapCellsToRow(rawDataRows[rawDataRows.length - 1])];
        }

        result.push({
          kind: 'table',
          table: {
            columns,
            rows: bodyCells.map(mapCellsToRow),
            bottomRows,
          },
        });
        continue;
      }
    }

    mdBuffer.push(line);
    i++;
  }

  flushMd();
  return result;
}
