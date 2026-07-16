// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLinksValidator from 'starlight-links-validator';

// IntuneCommander documentation — Astro Starlight.
// Project (not user) GitHub Pages site: served under /intunecommander-src.
// NOTE: `base` is case-sensitive and must equal the GitHub repo name exactly
// (the repo that hosts these Pages — the source repo, gellorg/intunecommander-src).
const base = '/intunecommander-src';

/**
 * Prepend the site `base` to root-absolute internal links in Markdown/MDX content.
 * Astro does NOT do this automatically, so authored links like `/sign-in/overview/`
 * would 404 under a project base. With this, content can use clean `/...` links.
 * (Starlight's own nav/sidebar/assets are already base-aware; this only touches
 * rendered Markdown content, and skips external, protocol-relative, hash, and
 * already-prefixed links.)
 */
function rehypeBaseLinks() {
	/** @param {any} node */
	const walk = (node) => {
		if (node.type === 'element' && node.tagName === 'a') {
			const href = node.properties && node.properties.href;
			if (
				typeof href === 'string' &&
				href.startsWith('/') &&
				!href.startsWith('//') &&
				href !== base &&
				!href.startsWith(base + '/')
			) {
				node.properties.href = base + href;
			}
		}
		if (node.children) node.children.forEach(walk);
	};
	/** @param {any} tree */
	return (tree) => walk(tree);
}

// https://astro.build/config
export default defineConfig({
	site: 'https://gellorg.github.io',
	base,
	markdown: { rehypePlugins: [rehypeBaseLinks] },
	integrations: [
		starlight({
			title: 'IntuneCommander',
			description:
				'Logs that know the cloud — a Windows-native tool that enriches local Intune/Entra logs with live tenant context, with full management CRUD and an audit time-machine.',
			customCss: ['./src/styles/fonts.css', './src/styles/cmx.css'],
			lastUpdated: true,
			social: [
				{
					icon: 'github',
					label: 'GitHub',
					href: 'https://github.com/gellorg/intunecommander-release',
				},
			],
			// Fail the build on broken internal links. The interactive Redoc page is a
			// static file (public/api.html), not a Starlight route, so it's excluded.
			plugins: [starlightLinksValidator({ exclude: ['**/api.html'] })],
			head: [
				{
					tag: 'meta',
					attrs: {
						property: 'og:image',
						content: 'https://gellorg.github.io/intunecommander-src/og.png',
					},
				},
				{
					tag: 'meta',
					attrs: { property: 'og:image:alt', content: 'IntuneCommander — logs that know the cloud' },
				},
				{ tag: 'meta', attrs: { name: 'twitter:card', content: 'summary_large_image' } },
				{
					tag: 'meta',
					attrs: {
						name: 'twitter:image',
						content: 'https://gellorg.github.io/intunecommander-src/og.png',
					},
				},
			],
			sidebar: [
				{
					label: 'Get started',
					items: [
						{ label: 'Download a release', slug: 'get-started/download' },
						{ label: 'Prerequisites', slug: 'get-started/prerequisites' },
						{ label: 'Install & build', slug: 'get-started/install' },
						{ label: 'First run', slug: 'get-started/first-run' },
						{ label: 'Smoke test', slug: 'get-started/smoke-test' },
					],
				},
				{
					label: 'Signing in',
					items: [
						{ label: 'How sign-in works', slug: 'sign-in/overview' },
						{ label: 'Sign in step by step', slug: 'sign-in/sign-in' },
						{ label: 'Manage tenant profiles', slug: 'sign-in/manage-profiles' },
						{ label: 'Troubleshooting', slug: 'sign-in/troubleshooting' },
					],
				},
				{
					label: 'Using IntuneCommander',
					items: [
						{ label: 'Navigation', slug: 'using/navigation' },
						{ label: 'Browse & view', slug: 'using/browse-and-view' },
						{ label: 'Edit safely', slug: 'using/edit-safely' },
						{ label: 'Assignments', slug: 'using/assignments' },
						{ label: 'The time-machine', slug: 'using/time-machine' },
						{ label: 'Diagnostics', slug: 'using/diagnostics' },
						{ label: 'Bulk & lifecycle', slug: 'using/bulk' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'Architecture', slug: 'reference/architecture' },
						{ label: 'Ports & paths', slug: 'reference/ports-paths' },
						{ label: 'API reference', slug: 'reference/api' },
						{ label: 'Troubleshooting', slug: 'reference/troubleshooting' },
						{ label: 'Glossary', slug: 'reference/glossary' },
					],
				},
				{
					label: 'Developing',
					items: [
						{ label: 'Cutting a release', slug: 'develop/releasing' },
					],
				},
				{ label: 'Changelog', slug: 'changelog' },
			],
		}),
	],
});
