// @ts-check

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docs: [
    'getting-started',
    'generated-code',
    {
      type: 'category',
      label: 'Building blocks',
      collapsed: false,
      items: ['identifiers', 'value-objects', 'entities-and-aggregates', 'invariants', 'composite-keys', 'domain-events'],
    },
    {
      type: 'category',
      label: 'Modules and messaging',
      collapsed: false,
      items: ['modules', 'integration-events'],
    },
    {
      type: 'category',
      label: 'Integrations',
      collapsed: false,
      items: ['entity-framework', 'graphql', 'localization', 'testing'],
    },
    {
      type: 'category',
      label: 'Reference',
      collapsed: false,
      items: ['diagnostics', 'performance', 'migrating-to-3'],
    },
  ],
};

module.exports = sidebars;
