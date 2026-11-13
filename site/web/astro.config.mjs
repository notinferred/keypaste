import { existsSync, readFileSync } from 'node:fs';
import { defineConfig } from 'astro/config';
import { unified } from '@astrojs/markdown-remark';
import starlight from '@astrojs/starlight';
import mermaid from 'astro-mermaid';

const products = JSON.parse(readFileSync(new URL('./src/data/products.json', import.meta.url), 'utf8'));

const productLinks = (status) =>
  products
    .filter((product) => product.status === status)
    .map((product) => ({ label: product.name, link: `/products/${product.slug}/` }));

const comparisons = (...pages) => pages.map(([slug, label]) => ({ label, link: `/compare/${slug}/` }));

// Production serves a folder's index.html at the folder's address; astro dev serves a public file only at its full path.
const publicIndexes = {
  name: 'keypaste-public-indexes',
  configureServer(server) {
    server.middlewares.use((request, _response, next) => {
      const [path, query] = request.url.split('?');
      if (path.endsWith('/') && existsSync(new URL(`../public${path}index.html`, import.meta.url))) {
        request.url = `${path}index.html${query ? `?${query}` : ''}`;
      }
      next();
    });
  },
};

export default defineConfig({
  vite: { plugins: [publicIndexes] },
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
        'A centralized access manager on a KeePass file you own: passwords, project secrets and AI agents that have to ask, built to grow to teams and organizations.',
      logo: {
        light: '../../assets/brand/keypaste-wordmark-light.svg',
        dark: '../../assets/brand/keypaste-wordmark-dark.svg',
        replacesTitle: true,
      },
      favicon: '/favicon.svg',
      social: [{ icon: 'github', label: 'Source on GitHub', href: 'https://github.com/notinferred/keypaste' }],
      customCss: ['./src/styles/brand.css'],
      components: {
        Head: './src/components/Head.astro',
        Search: './src/components/Search.astro',
        SocialIcons: './src/components/SocialIcons.astro',
      },
      lastUpdated: false,
      sidebar: [
        {
          label: 'Start',
          items: [
            { label: 'Download', link: '/download/' },
            { label: 'How keypaste works', link: '/how-it-works/' },
            { label: 'Security model', link: '/how-it-works/security/' },
            { label: 'Guides and reference', link: '/docs/' },
          ],
        },
        {
          label: 'Products',
          items: [
            { label: 'All products', link: '/products/' },
            { label: 'Available now', items: productLinks('beta') },
            { label: 'Building for 1.0', items: productLinks('building') },
            { label: 'Planned', collapsed: true, items: productLinks('planned') },
          ],
        },
        {
          label: 'Compare',
          collapsed: true,
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
        {
          label: 'Reference',
          items: [
            { label: 'Where keypaste is going', link: '/vision/' },
            { label: 'Access format (ACAF v1.1)', link: '/acaf/v1.1/' },
          ],
        },
      ],
    }),
  ],
});
