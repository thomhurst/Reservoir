/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docsSidebar: [
    'intro',
    'installation',
    'quick-start',
    'ownership-rules',
    {
      type: 'category',
      label: 'API guide',
      items: [
        'api/object-pools',
        'api/collection-pools',
        'api/cancellation-token-sources',
      ],
    },
    'configuration',
    'design',
    'benchmarks',
  ],
};

export default sidebars;
