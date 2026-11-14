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
            {slug: 'terms-of-service', label: 'Terms of Service'},
            {slug: 'privacy-policy', label: 'Privacy Policy'},
            {label: 'Staff handbook', collapsed: true, items: [
                {slug: 'staff/working-hours'},
                {slug: 'staff/tickets', label: 'Ticket overview'},
                {slug: 'staff/tickets/complete-a-ticket'},
                {slug: 'staff/tickets/add-a-note'},
                {slug: 'staff/tickets/find-a-ticket', label: 'Find tickets and member history'},
                {slug: 'staff/tickets/review-tickets', label: 'Review tickets'},
                {slug: 'staff/tickets/change-a-restriction', label: 'Change or lift a restriction'},
                {slug: 'staff/tickets/hide-or-restore'},
            ]},
            {label: 'Server setup', items: [
                {slug: 'setup/tickets'},
                {slug: 'setup/working-hours'},
            ]},
            {label: 'Troubleshooting', items: [
                {slug: 'help/tickets'},
                {slug: 'help/working-hours'},
            ]},
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
                items: [{slug: 'commands/announce'}, {slug: 'commands/yoink'}, {slug: 'commands/automod'}],
            },
            {
                label: 'Administrator commands',
                items: [
                    {slug: 'commands/config-announcements'},
                    {slug: 'commands/config-tickets'},
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
