import { describe, expect, it } from 'vitest';
import { badgeOf, downloadLinkOf, isInlineImage, isInlineVideo, pictureOf, splitName, stemOf } from './file-view';

describe('file view', () => {
  it('saves a file by its link with the query download, after the query the link already has', () => {
    expect(downloadLinkOf('/files/1?sig=a')).toBe('/files/1?sig=a&download');
    expect(downloadLinkOf('/files/1')).toBe('/files/1?download');
  });

  it('plays MP4 alone as a video a link serves inline, and draws no video as an image', () => {
    expect(isInlineVideo('video/mp4')).toBe(true);
    expect(isInlineVideo('video/webm')).toBe(false);
    expect(isInlineVideo(null)).toBe(false);
    expect(isInlineImage('video/mp4')).toBe(false);
  });

  it('draws a video by its thumbnail alone, never by its own content', () => {
    const clip = { key: 1, name: 'clip.mp4', size: 10, contentType: 'video/mp4', url: '/files/1?sig=a', thumbUrl: null };

    expect(pictureOf(clip)).toBeNull();
    expect(pictureOf({ ...clip, thumbUrl: '/files/1t?sig=a' })).toBe('/files/1t?sig=a');
  });

  it('names a tile by the name without the extension its badge shows, and a name without one whole', () => {
    expect(stemOf('catalog-retro.webp')).toBe('catalog-retro');
    expect(badgeOf('catalog-retro.webp')).toBe('WEBP');
    expect(stemOf('archive.tar.gz')).toBe('archive.tar');
    expect(stemOf('README')).toBe('README');
    expect(stemOf('.env')).toBe('.env');
    expect(stemOf('draft.')).toBe('draft.');
  });

  it('splits a name at the last dot that is neither its first nor its last character', () => {
    expect(splitName('plan.pdf')).toEqual({ base: 'plan', extension: '.pdf' });
    expect(splitName('archive.tar.gz')).toEqual({ base: 'archive.tar', extension: '.gz' });
    expect(splitName('draft.')).toEqual({ base: 'draft.', extension: '' });
    expect(splitName('.env')).toEqual({ base: '.env', extension: '' });
    expect(splitName('README')).toEqual({ base: 'README', extension: '' });
    expect(splitName('')).toEqual({ base: '', extension: '' });
  });

  it('draws the badge of the same extension, and none where the name has none', () => {
    expect(badgeOf('archive.tar.gz')).toBe('GZ');
    expect(badgeOf('draft.')).toBe('');
    expect(badgeOf('.env')).toBe('');
    expect(badgeOf('README')).toBe('');
  });
});
