import type { FullConfig } from '@playwright/test';
import fs from 'fs';

const DEST = 'src/tests';

export default async function globalTeardown(config: FullConfig) {
  // Delete previously created temporary directory. Comment for re-running tests manually inside the extension.
  fs.rmSync(DEST, { recursive: true });
}
