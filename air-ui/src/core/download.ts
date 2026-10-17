import { HttpClient, type HttpContext, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { wallOf } from './time-zone';

/** A file an endpoint answered: its bytes and the name its `Content-Disposition` header gave. */
export interface DownloadedFile {
  readonly blob: Blob;
  readonly name: string;
}

/**
 * The file name of a `Content-Disposition` header: the encoded `filename*=UTF-8''…` form first, which carries any
 * character, then the plain `filename=`; the fallback when the header is absent, hidden by CORS or names nothing.
 */
export function fileNameOf(disposition: string | null | undefined, fallback: string): string {
  if (!disposition) {
    return fallback;
  }
  const encoded = /(?:^|;)\s*filename\*\s*=\s*[^';]*'[^';]*'([^;]+)/i.exec(disposition)?.[1];
  if (encoded) {
    try {
      return decodeURIComponent(encoded.trim());
    } catch {
      // A malformed escape: the plain form is still there
    }
  }
  const plain = /(?:^|;)\s*filename\s*=\s*(?:"((?:[^"\\]|\\.)*)"|([^;]+))/i.exec(disposition);
  const name = (plain?.[1]?.replace(/\\(.)/g, '$1') ?? plain?.[2])?.trim();
  return name || fallback;
}

/**
 * `name` with the moment `at` on the clock of `zone`, the user's, inserted before its extension as `_yyMMddHHmmss`,
 * each part two digits: `Project_42.zip` saved at 15:33:12 on 28 September 2026 is `Project_42_260928153312.zip`; a
 * name without an extension ends in the moment.
 */
export function stampedName(name: string, at: Date, zone: string): string {
  const two = (value: number): string => String(value).padStart(2, '0');
  const wall = wallOf(at, zone);
  const date = `${two(wall.year % 100)}${two(wall.month)}${two(wall.day)}`;
  const stamp = `${date}${two(wall.hour)}${two(wall.minute)}${two(wall.second)}`;
  const dot = name.lastIndexOf('.');
  return dot > 0 ? `${name.slice(0, dot)}_${stamp}${name.slice(dot)}` : `${name}_${stamp}`;
}

/**
 * Posts `body` to `url` and answers the file. A refusal arrives as a blob too, because the response type is chosen
 * before the request: it is parsed back into the problem JSON, so the caller handles it like any other refusal.
 */
export async function fetchFile(
  http: HttpClient,
  url: string,
  body: unknown,
  fallbackName: string,
  options?: { context?: HttpContext },
): Promise<DownloadedFile> {
  try {
    const response = await firstValueFrom(http.post(url, body, { ...options, observe: 'response', responseType: 'blob' }));
    return {
      blob: response.body ?? new Blob(),
      name: fileNameOf(response.headers.get('Content-Disposition'), fallbackName),
    };
  } catch (error: unknown) {
    throw error instanceof HttpErrorResponse && error.error instanceof Blob ? await withProblem(error) : error;
  }
}

/** Hands the file to the browser through an object URL that lives only as long as the click needs it. */
export function saveFile(file: DownloadedFile, doc: Document = document): void {
  const href = URL.createObjectURL(file.blob);
  const link = doc.createElement('a');
  link.href = href;
  link.download = file.name;
  link.hidden = true;
  doc.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(href));
}

async function withProblem(error: HttpErrorResponse): Promise<HttpErrorResponse> {
  let problem: unknown = null;
  try {
    problem = JSON.parse(await textOf(error.error as Blob));
  } catch {
    // Not JSON: the status alone describes the refusal
  }
  return new HttpErrorResponse({
    error: problem,
    headers: error.headers,
    status: error.status,
    statusText: error.statusText,
    url: error.url ?? undefined,
  });
}

/** `FileReader` rather than `Blob.text()`: every browser and the test DOM have it. */
function textOf(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(reader.error ?? new Error('The answer could not be read.'));
    reader.readAsText(blob);
  });
}
