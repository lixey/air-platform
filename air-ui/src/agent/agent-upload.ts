import { firstValueFrom } from 'rxjs';
import type { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import type { Api } from '../core/api';
import type { TextsService } from '../core/texts.service';
import { optimizeImage } from '../file/file-optimize';
import type { UploadAccept } from '../file/file-upload-call';
import { imageFactsOf } from '../file/image-facts';
import { AGENT_IMAGE_RULES, type AgentAttachment, extractAgentErrorMessage } from './agent-models';

export const AGENT_UPLOAD_ACCEPT: UploadAccept = {
  accept: ['image/jpeg', 'image/png', 'image/webp', 'image/gif'],
  extensions: ['.jpg', '.jpeg', '.png', '.webp', '.gif'],
};

async function prepareAgentUploadFile(file: File): Promise<File> {
  const facts = await imageFactsOf(file);
  return optimizeImage(file, AGENT_IMAGE_RULES, facts);
}

export async function uploadAgentFiles(
  api: Api,
  alert: AlertService,
  texts: TextsService,
  files: FileList | readonly File[],
  onSaved: (saved: AgentAttachment) => void,
  setUploading: (uploading: boolean) => void,
): Promise<void> {
  const list = Array.from(files);
  if (list.length === 0) {
    return;
  }
  setUploading(true);
  try {
    for (const raw of list) {
      const prepared = await prepareAgentUploadFile(raw);
      const form = new FormData();
      form.append('file', prepared, prepared.name);
      const saved = await firstValueFrom(api.post('agent/upload', form, shownByCaller()));
      onSaved(saved);
    }
  } catch (err) {
    alert.error(extractAgentErrorMessage(err, texts.of('AgentErrorUpload')));
  } finally {
    setUploading(false);
  }
}

export async function uploadSingleAgentFile(
  api: Api,
  alert: AlertService,
  texts: TextsService,
  file: File,
  setUploading: (uploading: boolean) => void,
): Promise<AgentAttachment | null> {
  setUploading(true);
  try {
    const prepared = await prepareAgentUploadFile(file);
    const form = new FormData();
    form.append('file', prepared, prepared.name);
    return await firstValueFrom(api.post('agent/upload', form, shownByCaller()));
  } catch (err) {
    alert.error(extractAgentErrorMessage(err, texts.of('AgentErrorUpload')));
    return null;
  } finally {
    setUploading(false);
  }
}
