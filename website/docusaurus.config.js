// @ts-check
import {themes as prismThemes} from 'prism-react-renderer';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Reservoir',
  tagline: 'High-performance object pooling that becomes your code',
  url: 'https://thomhurst.github.io',
  baseUrl: '/Reservoir/',
  organizationName: 'thomhurst',
  projectName: 'Reservoir',
  onBrokenLinks: 'throw',
  markdown: {
    hooks: {onBrokenMarkdownLinks: 'throw'},
  },
  i18n: {defaultLocale: 'en', locales: ['en']},
  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/thomhurst/Reservoir/tree/main/website/',
          showLastUpdateTime: true,
        },
        blog: false,
        theme: {customCss: './src/css/custom.css'},
        sitemap: {changefreq: 'weekly', priority: 0.5},
      }),
    ],
  ],
  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      image: 'img/og.png',
      metadata: [
        {name: 'theme-color', content: '#071521'},
        {name: 'keywords', content: '.NET, C#, object pool, allocation-free, source package, performance'},
        {name: 'twitter:card', content: 'summary_large_image'},
      ],
      colorMode: {defaultMode: 'dark', respectPrefersColorScheme: true},
      navbar: {
        title: 'Reservoir',
        items: [
          {type: 'docSidebar', sidebarId: 'docsSidebar', position: 'left', label: 'Docs'},
          {to: '/docs/api/object-pools', label: 'API', position: 'left'},
          {to: '/docs/benchmarks', label: 'Benchmarks', position: 'left'},
          {href: 'https://github.com/thomhurst/Reservoir', label: 'GitHub', position: 'right', className: 'navbar-github-link'},
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Learn',
            items: [
              {label: 'Install', to: '/docs/installation'},
              {label: 'Quick start', to: '/docs/quick-start'},
              {label: 'Ownership rules', to: '/docs/ownership-rules'},
            ],
          },
          {
            title: 'Reference',
            items: [
              {label: 'Object pools', to: '/docs/api/object-pools'},
              {label: 'Collection pools', to: '/docs/api/collection-pools'},
              {label: 'Configuration', to: '/docs/configuration'},
            ],
          },
          {
            title: 'Project',
            items: [
              {label: 'GitHub', href: 'https://github.com/thomhurst/Reservoir'},
              {label: 'NuGet', href: 'https://www.nuget.org/packages/Reservoir'},
              {label: 'MIT license', href: 'https://github.com/thomhurst/Reservoir/blob/main/LICENSE'},
            ],
          },
        ],
        copyright: `Reservoir · Built in the open · ${new Date().getFullYear()}`,
      },
      prism: {
        theme: prismThemes.github,
        darkTheme: prismThemes.dracula,
        additionalLanguages: ['csharp', 'bash', 'markup'],
      },
    }),
};

export default config;
