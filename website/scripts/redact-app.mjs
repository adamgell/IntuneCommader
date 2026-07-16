// Redact + export the signed-in captures for the docs.
// Sources live in .smoke (untracked); outputs go to src/assets/screenshots.
// Usage: node website/scripts/redact-app.mjs
import sharp from 'sharp';
import { mkdir } from 'node:fs/promises';

const smoke = 'F:/Repo/cmProjectX/.smoke';
const outDir = 'F:/Repo/cmProjectX/website/src/assets/screenshots';
await mkdir(outDir, { recursive: true });

// app-1: signed-in Applications list + JSON detail. No tenant IDs / secrets /
// UPNs (just generic commercial app names + a public MSI product code) → as-is.
await sharp(`${smoke}/app-1-signin.png`)
	.png({ compressionLevel: 9 })
	.toFile(`${outDir}/browse-applications.png`);

// app-2: Compliance Policies. Replace the one personal-named policy row
// ("AdamGell-ManagedDevice-…") with a generic name, filled with the sampled
// row background so the swap is seamless.
const A2 = `${smoke}/app-2-compliance.png`;
const { data: bg } = await sharp(A2)
	.extract({ left: 360, top: 360, width: 1, height: 1 })
	.raw()
	.toBuffer({ resolveWithObject: true });
const bgHex = '#' + [...bg.slice(0, 3)].map((v) => v.toString(16).padStart(2, '0')).join('');
const ow = 306;
const oh = 30;
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${ow}" height="${oh}">
  <rect width="${ow}" height="${oh}" fill="${bgHex}"/>
  <text x="12" y="19" font-family="'Segoe UI Variable Text','Segoe UI',sans-serif" font-size="14" font-weight="600" fill="#ededed">iOS - Device - Compliance Baseline</text>
</svg>`;
await sharp(A2)
	.composite([{ input: Buffer.from(svg), left: 340, top: 365 }])
	.png({ compressionLevel: 9 })
	.toFile(`${outDir}/compliance-detail.png`);

console.log('bg', bgHex, '→ wrote browse-applications.png + compliance-detail.png');
