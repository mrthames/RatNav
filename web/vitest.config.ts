import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

/**
 * Test configuration, kept apart from `vite.config.ts` on purpose.
 *
 * Vite 8 types its plugins through rolldown and vitest types its own config through rollup, and the
 * two disagree about `PluginContextMeta`. Importing `vitest/config` into the build config makes
 * `tsc -b` fail on a conflict that has nothing to do with either the build or the tests. Two files,
 * two type worlds, no argument.
 *
 * This file is not in any tsconfig `include`, so it is not type-checked. That is tolerable for
 * config: a mistake here fails the moment tests run, which is the same feedback a type error would
 * have given, and only ever on the machine of whoever made it.
 */
export default defineConfig({
  plugins: [react()],

  test: {
    // jsdom rather than a browser: what is worth checking here is behaviour — which key does what,
    // what happens when the service says no — and none of that needs a real renderer. The parts
    // that genuinely need a browser are the map canvas and the overlay, and neither is testable
    // this way at any price worth paying.
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,

    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      include: ['src/**/*.{ts,tsx}'],
      // main.tsx only mounts the app, and a test file's own coverage says nothing.
      exclude: ['src/main.tsx', 'src/test/**', 'src/**/*.test.{ts,tsx}'],
    },
  },
})
