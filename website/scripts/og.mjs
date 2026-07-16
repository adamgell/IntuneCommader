// Generate the Open Graph / social-card image (1200×630) for link previews.
// Usage: node website/scripts/og.mjs
import sharp from 'sharp';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(here, '..', 'public', 'og.png');
const W = 1200;
const H = 630;

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">
  <rect width="${W}" height="${H}" fill="#111111"/>
  <rect x="0" y="0" width="10" height="${H}" fill="#007768"/>
  <g transform="translate(96,92) scale(3.0)">
    <rect x="6" y="14" width="26" height="5" rx="2.5" fill="#009688"/>
    <rect x="6" y="26" width="34" height="5" rx="2.5" fill="#009688"/>
    <rect x="6" y="38" width="20" height="5" rx="2.5" fill="#00C6BC"/>
    <path d="M40 28.5 C62 28.5 64 16 76 16" fill="none" stroke="#4DA3FF" stroke-width="2" opacity="0.85"/>
    <path d="M40 28.5 C64 28.5 66 29 82 29" fill="none" stroke="#4DA3FF" stroke-width="2" opacity="0.85"/>
    <path d="M40 28.5 C62 28.5 64 42 74 42" fill="none" stroke="#4DA3FF" stroke-width="2" opacity="0.7"/>
    <circle cx="76" cy="16" r="4.6" fill="#0078D4"/>
    <circle cx="82" cy="29" r="5.4" fill="#4DA3FF"/>
    <circle cx="74" cy="42" r="4" fill="#0078D4"/>
  </g>
  <text x="94" y="408" font-family="Bahnschrift, 'Segoe UI Variable Display', 'Segoe UI', sans-serif" font-size="120" font-weight="600">
    <tspan fill="#00C6BC">cm</tspan><tspan fill="#ffffff">ProjectX</tspan>
  </text>
  <text x="100" y="476" font-family="'Segoe UI', sans-serif" font-size="40" fill="#c6c6c6">Logs that know the cloud.</text>
  <text x="100" y="556" font-family="'Segoe UI', sans-serif" font-size="26" fill="#7a7a7a">Windows-native Intune management &amp; diagnostics</text>
</svg>`;

await sharp(Buffer.from(svg)).png({ compressionLevel: 9 }).toFile(out);
console.log('wrote', out);
