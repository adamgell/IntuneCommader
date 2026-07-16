# IntuneCommander documentation site

Public docs for IntuneCommander, built with [Astro Starlight](https://starlight.astro.build) and
deployed to GitHub Pages at **https://intunecommander.com**.

## Run it locally

```powershell
npm install            # first time only
npm run dev            # http://localhost:4321/
```

Before pushing, validate the production build:

```powershell
npm run build && npm run preview
```

## Structure

```
src/
  content/docs/        the pages (Markdown / MDX), grouped by section
  assets/              logo lockups + screenshots/ (web-optimised, REDACTED)
  styles/cmx.css       brand theme (maps docs/design-system/tokens.css → Starlight)
  styles/fonts.css     font stacks + display face for headings
astro.config.mjs       site/base, sidebar, logo, rehype base-link fixer
scripts/redact.mjs     screenshot redaction + export tool
```

- Pages live in `src/content/docs/`; the file path is the URL. Add a page, then list it in the
  `sidebar` in `astro.config.mjs`.
- **Internal links:** write them root-absolute, e.g. `[Sign in](/sign-in/overview/)`. The site is
  served at the domain root (`base: '/'`), so these resolve as-is. (If the site ever moves to a
  project Pages sub-path, the rehype plugin in `astro.config.mjs` prefixes the base at build time.)
- **Components** (`<Steps>`, `<Tabs>`, `<Card>`, …) require an `.mdx` file; plain prose can be `.md`.
  Asides (`:::note`, `:::tip`, `:::caution`) work in both.

## Screenshots & redaction (required for the public site)

**Never commit a screenshot containing a real tenant ID, app ID, secret, device name, or UPN.**
The source captures live in `.smoke/` (untracked) and must be redacted before they enter
`src/assets/screenshots/`.

`scripts/redact.mjs` is the redaction tool. It uses `sharp` to redraw the **Tenant profile**
dropdown over the real GUID with a fake one (`workspace — 1111…`) and exports web-sized images.
To re-run after a new capture:

```powershell
node scripts/redact.mjs
```

Checklist before adding any image:

1. Redact every tenant/app GUID, secret, device name, and UPN (use a `1111…` placeholder GUID).
2. Capture in **dark theme**, then export at a sensible width (~1600px for full-window shots).
3. Use descriptive alt text — it's read by screen readers and indexed by the site search.

> Captures that need a signed-in app against a live tenant (signed-in status bar, device-code
> panel, a list+detail surface, the diff/confirm dialog) should be taken on a demo/throwaway
> tenant and run through the same redaction checklist.
