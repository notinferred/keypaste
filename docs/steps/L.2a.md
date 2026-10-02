# L.2a — Build keypaste.com's content pages on Astro

Completed 2026-10-02, source and site; probe run 37025248626 built and checked the pages, and Cloudflare build `f3fca2e2` built them from `65e04fe` with the committed lockfile. Dev run 37025563723 was cancelled at the founder's direction: a website release is gated by its own build, not the product suite.

## Amendments

The founder selected L.2 ahead of L.1 ("finish everything else, the website, properly, with navigation to each product"), so it split. L.2a builds the content pages now; L.2b keeps the move of the guides L.1 rewrites and the content security policy (D-0402).

## What it built

- `site/web`, an Astro 7.3.5 and Starlight 0.42.5 project with its own lockfile, building into `site/dist` with `site/public` copied in unchanged. It has a products overview and 11 product pages, How it works and a security model, a comparison overview with two design-level tables and 14 competitor pages, the vision, and a docs hub that links to the guides in the repository.
- Each product's label comes from `site/web/src/data/products.json`, which drives the sidebar badges, the products table and each page's status line.
- Diagrams are fenced `mermaid` blocks drawn by astro-mermaid 2.1.0 with Mermaid 11.17.2, some with animated edges that stop under reduced motion. Astro 7's default Markdown processor, Sätteri, runs no remark plugins, so the config selects the unified processor from `@astrojs/markdown-remark` 7.3.1.
- `site/wrangler.jsonc` builds `web/` before every deploy and serves `./dist`; `site/.node-version` asks Cloudflare for Node 22.
- The home page links to every product page and to How it works, Compare and Docs, and still runs no JavaScript.
- `verify-keepassxc-fields.sh` derives its Worker configuration without the build and serves `public/`, which holds the share viewer.
- `ci.yml` ignores `site/web/**` and `site/.node-version` on pushes.

## Evidence

- Probe run 37025248626 used a temporary workflow on `site-astro`, deleted before landing. On Node 22.23.3, `npm install` wrote the lockfile, and `astro build` built 32 pages in 3.81 s. Mermaid's own parser, under jsdom, parsed all 53 blocks in `site/web/src/content` and `docs/research`, with none failing.
- Its artifact was checked file by file. `index.html`, `s/index.html`, `s/viewer.js`, `s/share-crypto.js`, `thanks/index.html` and `_headers` are byte-identical to `public/`. Across 35 HTML pages, every internal link and anchor resolves. The sidebar carries 3 Beta, 3 Building and 5 Planned badges. Only pages with a `pre.mermaid` block load Mermaid, and the Pagefind search index was built.
- Cloudflare built `d256195`, which had no lockfile, and its install failed as intended, deploying nothing. Build `f3fca2e2` of `65e04fe` succeeded.

## Decisions

- D-0402: `site/web` is a separate project, built by wrangler's build step. The fields gate stays apart from it, and Cloudflare's build of each commit gates the content pages.
- Binding only this step: Mermaid stays on 11.x because astro-mermaid 2.1.0 accepts `^10 || ^11`. Competitor pages carry no prices, versions or dates, and the comparison overview names the month its facts were checked, October 2026.

## Limits and follow-ups

- The content pages have no content security policy yet: what Mermaid needs under a strict one is unverified without a browser (L.2b).
- Nobody has looked at the pages in a browser yet: rendering, the theme switch, the diagram animation and search are unverified.
- Branch pushes that touch `site/` build in Cloudflare, so the non-production deploy command must only upload a version; site/README now says so, and the dashboard setting is unconfirmed.
- An unknown path still gets the Worker's plain 404, not Starlight's 404 page.
- The home page repeats the labels from `products.json`, so a release changes both.
- There is no CHANGELOG entry: the site is not part of a product release.
