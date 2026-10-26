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
        pagination: false,
        sidebar: [{slug: 'index', label: 'Introduction'}, {
            label: "Commands",
            items: [{autogenerate: {directory: "commands"}}]
        }, {label: 'ratgirl.dev', link: 'https://ratgirl.dev'},],
    }),],
});
