import {defineConfig} from 'astro/config';
import starlight from '@astrojs/starlight';

const site = process.env.DOCS_SITE ?? 'https://math.ratgirl.dev';
const base = process.env.DOCS_BASE ?? '/sylvan';

export default defineConfig({
    site,
    base,
    trailingSlash: 'always',
    output: 'static',
    integrations: [starlight({
        title: 'Sylvan',
        description: 'A short guide to using Sylvan in Discord.',
        defaultLocale: 'root',
        locales: {root: {label: 'English', lang: 'en-GB'}},
        customCss: ['./src/styles/theme.css'],
        components: {SiteTitle: './src/components/SiteTitle.astro'},
        pagination: false,
        sidebar: [
            {slug: 'index', label: 'Introduction'},
            {label: 'Features', items: [
                {slug: 'features', label: 'Overview'},
                {slug: 'features/colours'},
                {slug: 'features/reaction-roles'},
                {slug: 'features/persistence'},
                {slug: 'features/working-hours'},
                {slug: 'features/sylvanmail'},
                {slug: 'features/tickets'},
                {slug: 'features/logging'},
                {slug: 'features/automod'},
                {slug: 'features/image-spam'},
            ]},
            {slug: 'commands', label: 'Command overview'},
            {
                label: 'User commands',
                items: [
                    {slug: 'commands/hello'},
                    {slug: 'commands/color'},
                ],
            },
            {
                label: 'Moderator commands',
                items: [{slug: 'commands/tickets'}, {slug: 'commands/working-hours'}],
            },
            {
                label: 'Senior moderator commands',
                items: [{slug: 'commands/yoink'}, {slug: 'commands/automod'}],
            },
            {
                label: 'Administrator commands',
                items: [
                    {label: '/config tickets', link: new URL(`${base.replace(/\/$/, '')}/commands/tickets/#administrator-configuration`, site).href},
                    {slug: 'commands/persistence'},
                    {slug: 'commands/color-admin'},
                    {slug: 'commands/automod-admin'},
                    {label: '/config sylvanmail', link: new URL(`${base.replace(/\/$/, '')}/commands/sylvanmail/#administrator-configuration`, site).href},
                    {slug: 'commands/role-react'},
                    {slug: 'commands/logging'},
                    {slug: 'commands/spam'},
                    {slug: 'commands/working-conditions'},
                ],
            },
        ],
    }),],
});
