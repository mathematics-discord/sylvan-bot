import {defineConfig} from 'astro/config';
import starlight from '@astrojs/starlight';
import sitemap from '@astrojs/sitemap';
import {isLegacyUrl} from './src/redirects.mjs';

const site = process.env.DOCS_SITE ?? 'https://math.ratgirl.dev';
const base = process.env.DOCS_BASE ?? '/sylvan';

export default defineConfig({
    site,
    base,
    trailingSlash: 'always',
    output: 'static',
    integrations: [starlight({
        title: 'Sylvan',
        description: 'Member guides, staff procedures and command reference for Sylvan on the Mathematics Discord server.',
        defaultLocale: 'root',
        locales: {root: {label: 'English', lang: 'en-GB'}},
        customCss: ['./src/styles/theme.css'],
        components: {SiteTitle: './src/components/SiteTitle.astro'},
        pagination: false,
        sidebar: [
            {slug: 'index', label: 'Handbook home'},
            {label: 'Member guides', items: [
                {slug: 'features/sylvanmail'},
                {slug: 'features/colours'},
                {slug: 'features/reaction-roles'},
                {slug: 'features/persistence', label: 'Roles after rejoining'},
            ]},
            {label: 'Staff handbook', collapsed: true, items: [
                {label: 'Moderation tickets', collapsed: true, items: [
                    {slug: 'staff/tickets', label: 'Ticket handbook'},
                    {slug: 'features/tickets', label: 'Ticket records and permissions'},
                    {slug: 'staff/tickets/complete-a-ticket'},
                    {slug: 'staff/tickets/add-a-note'},
                    {slug: 'staff/tickets/find-a-ticket'},
                    {slug: 'staff/tickets/review-tickets'},
                    {slug: 'staff/tickets/change-a-restriction'},
                    {slug: 'staff/tickets/hide-or-restore'},
                    {slug: 'help/tickets'},
                ]},
                {slug: 'staff/sylvanmail'},
                {slug: 'staff/working-hours'},
                {slug: 'features/logging', label: 'Read message logs'},
                {slug: 'features/automod'},
                {slug: 'commands/automod'},
                {slug: 'features/image-spam', label: 'Investigate an attachment ban'},
                {slug: 'commands/yoink', label: 'Copy a custom emoji'},
            ]},
            {label: 'Server setup', collapsed: true, items: [
                {slug: 'setup/tickets'},
                {slug: 'setup/sylvanmail'},
                {slug: 'setup/working-hours'},
                {slug: 'commands/color-admin'},
                {slug: 'commands/role-react'},
                {slug: 'commands/persistence'},
                {slug: 'commands/logging', label: 'Configure message logs'},
                {slug: 'commands/automod-admin'},
                {slug: 'commands/spam', label: 'Configure attachment-spam detection'},
            ]},
            {label: 'Command reference', collapsed: true, items: [
                {slug: 'commands', label: 'All commands and shortcuts'},
                {slug: 'commands/tickets'},
                {slug: 'commands/config-tickets'},
                {slug: 'commands/color'},
                {slug: 'commands/hello'},
            ]},
        ],
    }), sitemap({filter: (url) => !isLegacyUrl(url, base)})],
});
