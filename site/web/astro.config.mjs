import { readFileSync } from 'node:fs';
import { defineConfig } from 'astro/config';
import { unified } from '@astrojs/markdown-remark';
import starlight from '@astrojs/starlight';
import mermaid from 'astro-mermaid';
import { statuses } from './src/data/status.js';

const products = JSON.parse(readFileSync(new URL('./src/data/products.json', import.meta.url), 'utf8'));

const productLinks = (status) =>
  products
    .filter((product) => product.status === status)
    .map((product) => ({
      label: product.name,
      link: `/products/${product.slug}/`,
      badge: { text: statuses[status].label, variant: statuses[status].variant },
    }));

const comparisons = (...pages) => pages.map(([slug, label]) => ({ label, link: `/compare/${slug}/` }));

export default defineConfig({
  site: 'https://keypaste.com',
  // The home page, the share viewer and the fonts stay plain files in site/public, copied as they are.
  publicDir: '../public',
  outDir: '../dist',
  // Sätteri, Astro's default Markdown processor, runs no remark or rehype plugins, and astro-mermaid is one.
  markdown: { processor: unified() },
  integrations: [
    mermaid({
      autoTheme: true,
      enableLog: false,
      mermaidConfig: { fontFamily: '"Instrument Sans", system-ui, sans-serif' },
    }),
    starlight({
      title: 'keypaste',
      description:
        'A local password manager on the KeePass file you already own, with project secrets and AI agents that have to ask.',
      logo: {
        light: '../../assets/brand/keypaste-wordmark-light.svg',
        dark: '../../assets/brand/keypaste-wordmark-dark.svg',
        replacesTitle: true,
      },
      favicon: '/favicon.svg',
      social: [{ icon: 'github', label: 'Source on GitHub', href: 'https://github.com/notinferred/keypaste' }],
      customCss: ['./src/styles/brand.css'],
      lastUpdated: false,
      sidebar: [
        {
          label: 'Products',
          items: [
            { label: 'All products', link: '/products/' },
            ...productLinks('beta'),
            ...productLinks('building'),
            ...productLinks('planned'),
          ],
        },
        {
          label: 'How it works',
          items: [
            { label: 'Overview', link: '/how-it-works/' },
            { label: 'Security model', link: '/how-it-works/security/' },
          ],
        },
        {
          label: 'Compare',
          items: [
            { label: 'Side by side', link: '/compare/' },
            {
              label: 'KeePass apps',
              collapsed: true,
              items: comparisons(
                ['keepassxc', 'KeePassXC'],
                ['keepassium', 'KeePassium'],
                ['strongbox', 'Strongbox'],
                ['keepassdx', 'KeePassDX'],
              ),
            },
            {
              label: 'Hosted password managers',
              collapsed: true,
              items: comparisons(
                ['1password', '1Password'],
                ['bitwarden', 'Bitwarden'],
                ['proton-pass', 'Proton Pass'],
                ['keeper', 'Keeper'],
              ),
            },
            {
              label: 'Secrets platforms',
              collapsed: true,
              items: comparisons(
                ['infisical', 'Infisical'],
                ['phase', 'Phase'],
                ['doppler', 'Doppler'],
                ['vault-openbao', 'Vault and OpenBao'],
              ),
            },
            {
              label: 'Tools in your repository',
              collapsed: true,
              items: comparisons(['varlock', 'Varlock'], ['dotenvx', 'dotenvx']),
            },
          ],
        },
        { label: 'Vision', link: '/vision/' },
        { label: 'Docs', link: '/docs/' },
      ],
    }),
  ],
});
