import type { FileUploadLimitsDto } from '../src/core/generated/contracts';

export function uploadLimits(overrides: Partial<FileUploadLimitsDto> = {}): FileUploadLimitsDto {
  return {
    accept: ['image/png', 'application/pdf'],
    extensions: ['.png', '.pdf'],
    maxBytes: 1024 * 1024,
    sourceMaxBytes: 1024 * 1024,
    maxFiles: 100,
    maxTotalBytes: 0,
    images: 'keep',
    imageMaxSide: 2560,
    imageQuality: 0.8,
    maxPixels: 40_000_000,
    thumbnailMaxBytes: 64 * 1024,
    ...overrides,
  };
}
