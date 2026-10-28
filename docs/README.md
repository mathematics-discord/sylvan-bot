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

Theme styling lives in `src/styles/theme.css`, using Starlight's built-in
appearance handling. Source Sans 3 variable fonts are self-hosted in
`src/assets/fonts/source-sans-3/`; their licence and source notices are in
`public/fonts/source-sans-3/` so they accompany the build. Astro bundles
the fonts with base-aware URLs. The Sylvan wordmark keeps its Georgia font,
and code keeps its monospace font.

## Deployment

The production release workflow calls `.github/workflows/deploy-docs.yml`
after publishing the scanned release commit to `master`. The docs workflow
checks, builds and publishes that exact commit, independently of the bot's
image build and deployment. Ordinary pushes do not deploy the docs.

The release version is passed as `DOCS_RELEASE` at build time and displayed
beside the site title. Local builds omit it unless that variable is set.

The repository secrets `DOCS_DEPLOY_SSH_KEY` and `DOCS_DEPLOY_KNOWN_HOSTS`
provide the dedicated docs SSH key and pinned VPS host key. The VPS account
can only stream a site archive to `/usr/local/sbin/sylvan-docs-deploy`,
installed from `deploy/receive.sh`. Updates to that receiver need to be
installed by a VPS administrator.

Releases live in `/var/www/math/sylvan/releases/`. Deployment switches the
`current` link atomically and restores the previous release if the public
page does not match. Apache and certificate configuration are unchanged.

## Role mentions

Role names and colours live in `src/data/roles.json`, keyed by Discord role
ID. Keep IDs quoted as strings so their digits are preserved:

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

The component adds the `@`, name, and colour. Unknown IDs fail the build
with a message pointing to the configuration file. Update that file when
a role changes; it is not synchronised with Discord automatically.

The shared `.role-mention` rules in `src/styles/theme.css` blend each role
colour with the page palette and adjust the text for light mode. These
are text labels, not links or live Discord mentions.
