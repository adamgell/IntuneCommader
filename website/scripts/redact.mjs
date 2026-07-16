// Redact the real tenant GUID from the captured app screenshot and export
// web-sized, brand-safe images for the docs. Source stays in .smoke (untracked).
//
// Usage: node website/scripts/redact.mjs
import sharp from 'sharp';
import { mkdir } from 'node:fs/promises';

const src = 'F:/Repo/cmProjectX/.smoke/login_max.png';
const outDir = 'F:/Repo/cmProjectX/website/src/assets/screenshots';
await mkdir(outDir, { recursive: true });

// Redraw the "Tenant profile" dropdown over the real GUID with a demo value.
const ow = 376;
const oh = 36;
// Keep the original non-sensitive profile label; replace ONLY the real tenant GUID.
const demo = 'workspace — 11111111-1111-1111-1111-111111111111';
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${ow}" height="${oh}">
  <defs><clipPath id="c"><rect x="0" y="0" width="332" height="${oh}"/></clipPath></defs>
  <rect x="0.5" y="0.5" width="${ow - 1}" height="${oh - 1}" rx="5" fill="#272727" stroke="#3d3d3d"/>
  <text x="13" y="23" clip-path="url(#c)" font-family="'Segoe UI Variable Text','Segoe UI',sans-serif" font-size="15" fill="#d6d6d6">${demo}</text>
  <path d="M346 14 L352 20 L358 14" fill="none" stroke="#b0b0b0" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>
</svg>`;

const redacted = await sharp(src)
	.composite([{ input: Buffer.from(svg), left: 1398, top: 712 }])
	.png()
	.toBuffer();

// Full-app overview, resized for the web.
await sharp(redacted)
	.resize({ width: 1656 })
	.png({ compressionLevel: 9 })
	.toFile(`${outDir}/app-overview.png`);

// Close-up of the Sign-in card for the sign-in pages.
await sharp(redacted)
	.extract({ left: 1356, top: 545, width: 672, height: 540 })
	.resize({ width: 560 })
	.png({ compressionLevel: 9 })
	.toFile(`${outDir}/sign-in-card.png`);

console.log('wrote app-overview.png and sign-in-card.png to', outDir);
