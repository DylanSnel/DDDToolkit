// @ts-check
const path = require('path');
const { themes: prismThemes } = require('prism-react-renderer');
const githubLinks = require('./src/remark/githubLinks');

const repoRoot = path.resolve(__dirname, '..');
const docsDir = path.join(repoRoot, 'docs');

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'DDDToolkit',
  tagline: 'Source-generated building blocks for Domain-Driven Design in .NET',
  favicon: 'img/logo.svg',

  url: 'https://dylansnel.github.io',
  baseUrl: '/DDDToolkit/',
  organizationName: 'DylanSnel',
  projectName: 'DDDToolkit',
  trailingSlash: false,

  onBrokenLinks: 'throw',

  stylesheets: [
    {
      href: 'https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&family=JetBrains+Mono:wght@400;500;600&display=swap',
      type: 'text/css',
    },
  ],

  markdown: {
    // The docs are plain Markdown, full of C# generics such as IInvariant<T>; read as MDX they would be JSX.
    format: 'detect',
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          // One set of docs: the repository's docs folder, which GitHub renders as well.
          path: docsDir,
          routeBasePath: 'docs',
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/DylanSnel/DDDToolkit/edit/main/docs/',
          // Before Docusaurus resolves the links between docs, so it never sees the ones that leave docs/.
          beforeDefaultRemarkPlugins: [[githubLinks, { docsDir, repoRoot }]],
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  plugins: [
    // llms.txt, llms-full.txt and every page as Markdown, for language models; see the plugin for the format.
    ['./src/plugins/llms', { docsDir, repoRoot, routeBasePath: 'docs', sidebar: require('./sidebars').docs }],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      colorMode: {
        defaultMode: 'dark',
        respectPrefersColorScheme: true,
      },
      docs: {
        sidebar: {
          hideable: true,
        },
      },
      navbar: {
        title: 'DDDToolkit',
        hideOnScroll: false,
        logo: {
          alt: 'DDDToolkit',
          src: 'img/logo.svg',
        },
        items: [
          { type: 'docSidebar', sidebarId: 'docs', position: 'left', label: 'Docs' },
          { href: 'https://github.com/DylanSnel/DDDToolkit/tree/main/Examples', label: 'Examples', position: 'left' },
          { href: 'https://www.nuget.org/packages?q=DDDToolkit', label: 'NuGet', position: 'right' },
          { href: 'https://github.com/DylanSnel/DDDToolkit', label: 'GitHub', position: 'right' },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Docs',
            items: [
              { label: 'Getting started', to: '/docs/getting-started' },
              { label: 'Modules', to: '/docs/modules' },
              { label: 'GraphQL', to: '/docs/graphql' },
            ],
          },
          {
            title: 'More',
            items: [
              { label: 'Examples', href: 'https://github.com/DylanSnel/DDDToolkit/tree/main/Examples' },
              { label: 'Changelog', href: 'https://github.com/DylanSnel/DDDToolkit/blob/main/CHANGELOG.md' },
              // A file the build writes rather than a route, so pathname:// keeps it out of the router.
              { label: 'llms.txt', href: 'pathname:///llms.txt' },
              { label: 'GitHub', href: 'https://github.com/DylanSnel/DDDToolkit' },
            ],
          },
        ],
        copyright: `DDDToolkit, MIT licensed. Built with Docusaurus.`,
      },
      prism: {
        theme: prismThemes.oneLight,
        darkTheme: prismThemes.oneDark,
        additionalLanguages: ['csharp', 'bash', 'json', 'graphql', 'sql', 'powershell'],
      },
    }),
};

module.exports = config;
