import { constructorParametersDownlevelTransform } from '@angular/compiler-cli/private/tooling';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { defineConfig } from 'vitest/config';

// The library through the link `demo/web/.air-ui`, by the link's path and never its target: bare imports then resolve
// from `demo/web/node_modules`, and the JIT plugin below recognises the library's files by this prefix.
const root = fileURLToPath(new URL('../../.air-ui/', import.meta.url));

export default defineConfig({
  root,
  // Vite writes its cache beside `node_modules`: the workspace's, never a folder inside the library.
  cacheDir: fileURLToPath(new URL('../../node_modules/.vite/air-ui', import.meta.url)),
  resolve: {
    // Keeps the link's path for every file behind it, so the library's imports find the workspace's single Angular.
    preserveSymlinks: true,
  },
  plugins: [
    {
      name: 'angular-jit-inputs',
      enforce: 'pre',
      transform(code, id) {
        if (!id.startsWith(`${root}src/`) || !id.endsWith('.ts') || !/@(?:Component|Directive|Injectable|Service)\b/.test(code)) {
          return;
        }
        // Vitest's plain TS transform does not emit Angular signal input/model metadata.
        // Apply Angular's own JIT transform to this file only, without a project build.
        const options: ts.CompilerOptions = { noResolve: true, noLib: true, target: ts.ScriptTarget.ESNext, experimentalDecorators: true };
        const source = ts.createSourceFile(id, code, ts.ScriptTarget.ESNext, true);
        const host = ts.createCompilerHost(options);
        host.getSourceFile = fileName => (fileName === id ? source : undefined);
        const program = ts.createProgram([id], options, host);
        const result = ts.transform(source, [constructorParametersDownlevelTransform(program)]);
        try {
          return { code: ts.createPrinter().printFile(result.transformed[0]), map: null };
        } finally {
          result.dispose();
        }
      },
    },
  ],
  test: {
    name: 'air-ui',
    watch: false,
    globals: true,
    // Each spec file in its own VM context within child processes: the zone a spec sets through `TZ` stays its process's,
    // which threads would share, and the specs run in less than half the time of `forks` (5.6 s against 13.0 s).
    pool: 'vmForks',
    // A bound on a spec that hangs, not on a loaded machine: the gate runs these specs beside the .NET tests, which take
    // every core, and a spec of 2 s alone then passes the default 5 s.
    testTimeout: 20_000,
    environment: 'jsdom',
    include: ['{src,tests}/**/*.{test,spec}.{js,mjs,cjs,ts,mts,cts,jsx,tsx}'],
    setupFiles: ['src/test-setup.ts'],
    // The user's clock of every spec: a zone behind UTC with its own clock changes; a spec proving another sets it with `inTimeZone`.
    env: { TZ: 'America/New_York' },
    reporters: ['default'],
    coverage: {
      reportsDirectory: fileURLToPath(new URL('../../coverage/air-ui', import.meta.url)),
    },
  },
});
