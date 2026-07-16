// Copy the API contract into public/ so the Redoc page can load it.
// Runs automatically before `npm run dev` and `npm run build` (see package.json).
import { copyFile, mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url)); // website/scripts
const src = resolve(here, '..', '..', 'contract', 'openapi.yaml');
const dest = resolve(here, '..', 'public', 'openapi.yaml');

await mkdir(dirname(dest), { recursive: true });
await copyFile(src, dest);
console.log('copied contract →', dest);
