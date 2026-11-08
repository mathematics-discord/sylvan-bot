# Public documentation

This is an independent Astro + Starlight site. Only Markdown and MDX in
`src/content/docs/` is published; the existing notes elsewhere in `docs/`
remain outside the site. Navigation is selected in `astro.config.mjs`.

Use Node.js 22.23.4 or newer. From this directory:

```sh
npm ci
npm run dev
npm run check
npm run build
```

The site is configured for `https://math.ratgirl.dev/sylvan/`. The static
output is `docs/dist/` relative to the repository root.

For `https://sylvan.mathematics.gg/`, build the same source with a separate
site URL and base path:

```sh
DOCS_SITE=https://sylvan.mathematics.gg DOCS_BASE=/ npm run build
```

Keep the two outputs separate. Navigation, canonical URLs, sitemaps, asset
URLs and Pagefind search settings are generated for each base path. A
server rewrite cannot make the legacy output into a root-path build.

Theme styling lives in `src/styles/theme.css`, using Starlight's built-in
appearance handling. Source Sans 3 variable fonts are self-hosted in
`src/assets/fonts/source-sans-3/`; their licence and source notices are in
`public/fonts/source-sans-3/` so they accompany the build. Astro bundles
the fonts with base-aware URLs. The Sylvan wordmark keeps its Georgia font,
and code keeps its monospace font.

## Deployment

`.github/workflows/deploy-docs.yml` publishes both hostnames automatically
when documentation, its workflow, or `version.json` changes on `master`.
Documentation deployment is independent of bot releases and does not wait
for the bot image build. Each queued run checks out the current `master`,
so a delayed run cannot publish an older documentation snapshot.

For a documentation-only retry, manually run **Deploy Sylvan documentation**
(`deploy-docs.yml`) on `master`. It publishes the latest `master` docs and
reads the displayed version from `version.json`; it does not create a bot
release or deploy the bot.
See [Preparing a release](releases.md) for the local batching procedure.

The version from `version.json` is passed as `DOCS_RELEASE` at build time and displayed
beside the site title. Local builds omit it unless that variable is set.

The repository secrets `DOCS_DEPLOY_SSH_KEY` and `DOCS_DEPLOY_KNOWN_HOSTS`
provide the dedicated docs SSH key and pinned VPS host key. The VPS account
can only stream a site archive to `/usr/local/sbin/sylvan-docs-deploy`,
installed from `deploy/receive.sh`. Updates to that receiver need to be
installed by a VPS administrator.

The workflow builds both variants. The existing SSH command `deploy`
publishes the legacy build to `/var/www/math/sylvan/releases/`. The command
`deploy-root` publishes the root build to `/var/www/sylvan/releases/`, using
the same restricted account and receiver. Root deployments reject archives
with the legacy canonical URL or base-path references before publishing.
Each deployment switches its own `current` link atomically and restores
the previous release if the served page does not match. Root verification
uses local hostname routing, following the HTTP-to-HTTPS redirect once it
is enabled. Public workflow verification checks both HTTPS hostnames.

The VPS receiver must support both commands before enabling this workflow.
Both hostnames have their own Apache configuration and Certbot certificate.
The legacy hostname retains its configuration and does not redirect to the
new hostname. The new hostname serves a root-path build with its own
navigation, assets, canonical URLs, sitemaps and Pagefind index.

## Role mentions

Role names and colours live in `src/data/roles.json`, keyed by Discord role
ID where known. The neutral Administrators placeholder uses the descriptive
key `administrators`; it is not a Discord ID. Keep IDs quoted as strings so their digits are preserved:

```json
{
  "1328097118644338779": {
    "name": "Senior Moderators",
    "color": "#ff8cd5"
  }
}
```

To mention a role, use an `.mdx` page and import `RoleMention` after its
frontmatter. For a page in `src/content/docs/commands/`:

```mdx
import RoleMention from '../../../components/RoleMention.astro';

Available to <RoleMention id="1328097118644338779" /> on Mathematics.
```

Use `"color": null` for a role with Discord's default colour. Its mention
uses the theme's neutral text colour in both light and dark mode.

The component adds the `@`, name, and colour. Unknown keys fail the build
with a message pointing to the configuration file. Update that file when
a role changes; it is not synchronised with Discord automatically.

The shared `.role-mention` rules in `src/styles/theme.css` blend each role
colour with the page palette and adjust the text for light mode. These
are text labels, not links or live Discord mentions.
