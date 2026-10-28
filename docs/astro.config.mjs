import {defineConfig} from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
    site: 'https://math.ratgirl.dev',
    base: '/sylvan',
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
            {slug: 'commands/sylvanmail'},
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
                items: [{slug: 'commands/working-hours'}],
            },
            {
                label: 'Senior moderator commands',
                items: [{slug: 'commands/yoink'}],
            },
            {
                label: 'Administrator commands',
                items: [
                    {slug: 'commands/color-admin'},
                    {label: '/config sylvanmail', link: 'https://math.ratgirl.dev/sylvan/commands/sylvanmail/#administrator-configuration'},
                    {slug: 'commands/role-react'},
                    {slug: 'commands/logging'},
                    {slug: 'commands/spam'},
                    {slug: 'commands/working-conditions'},
                ],
            },
        ],
    }),],
});
