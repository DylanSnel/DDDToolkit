// @ts-check
const path = require('path');
const { themes: prismThemes } = require('prism-react-renderer');
const githubAlerts = require('./src/remark/githubAlerts');
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
    // ```mermaid blocks become diagrams: here through the theme below, and on GitHub natively, so the
    // docs folder shows the same diagrams wherever it is read.
    mermaid: true,
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  themes: ['@docusaurus/theme-mermaid'],

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
          // Before Docusaurus resolves the links between docs, so it never sees the ones that leave docs/,
          // and before its admonitions plugin, which turns what githubAlerts makes into a banner.
          beforeDefaultRemarkPlugins: [[githubLinks, { docsDir, repoRoot }], githubAlerts],
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
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
              { label: 'GitHub', href: 'https://github.com/DylanSnel/DDDToolkit' },
            ],
          },
        ],
        copyright: `DDDToolkit, MIT licensed. Built with Docusaurus.`,
      },
      mermaid: {
        theme: { light: 'neutral', dark: 'dark' },
        options: {
          fontFamily: 'Inter, system-ui, sans-serif',
        },
      },
      prism: {
        theme: prismThemes.oneLight,
        darkTheme: prismThemes.oneDark,
        additionalLanguages: ['csharp', 'bash', 'json', 'graphql', 'sql', 'powershell'],
      },
    }),
};

module.exports = config;
