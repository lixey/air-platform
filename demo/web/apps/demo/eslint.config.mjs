import nx from '@nx/eslint-plugin';
import baseConfig from '../../eslint.config.mjs';

export default [
  ...nx.configs['flat/angular'],
  ...nx.configs['flat/angular-template'],
  ...baseConfig,
  {
    files: ['**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
      // Angular 22 defaults written out again (angular.md, rule 2)
      'no-restricted-syntax': [
        'error',
        {
          selector: "Property[key.name='changeDetection'][value.property.name='OnPush']",
          message: 'OnPush is the default in Angular 22.',
        },
        {
          selector: "Property[key.name='standalone'][value.value=true]",
          message: 'standalone is the default in Angular 22.',
        },
      ],
    },
  },
  {
    // Inline templates are linted as virtual HTML files; a label may wrap a control of the UI library
    files: ['**/*.html'],
    rules: {
      '@angular-eslint/template/no-any': 'error',
      '@angular-eslint/template/label-has-associated-control': [
        'error',
        { controlComponents: ['ui-text-box', 'ui-number-box', 'ui-temporal-box', 'ui-checkbox', 'ui-checkbox-set'] },
      ],
    },
  },
];
