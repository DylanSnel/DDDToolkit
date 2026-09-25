// @ts-check

/**
 * The order is the order a reader needs things in: the building blocks with no database, then storing
 * them, then modules and messages between them, then the other integrations, then reference.
 *
 * @type {import('@docusaurus/plugin-content-docs').SidebarsConfig}
 */
const sidebars = {
  docs: [
    'getting-started',
    'generated-code',
    {
      type: 'category',
      label: 'Building blocks',
      collapsed: false,
      items: ['identifiers', 'value-objects', 'entities-and-aggregates', 'invariants', 'domain-events', 'aggregate-design', 'testing'],
    },
    {
      type: 'category',
      label: 'Persistence',
      collapsed: false,
      items: ['entity-framework', 'composite-keys', 'event-delivery', 'supabase'],
    },
    {
      type: 'category',
      label: 'Modules and messaging',
      collapsed: false,
      items: ['modules', 'module-contracts', 'integration-events', 'transports'],
    },
    {
      type: 'category',
      label: 'Integrations',
      collapsed: false,
      items: ['graphql', 'fluent-validation', 'localization'],
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
