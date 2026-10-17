import nx from '@nx/eslint-plugin';
import baseConfig from '../../eslint.config.mjs';

export default [
  ...nx.configs['flat/angular'],
  ...nx.configs['flat/angular-template'],
  ...baseConfig,
  {
    files: ['**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': 'off',
      '@angular-eslint/component-selector': 'off',
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          argsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
          ignoreRestSiblings: true,
        },
      ],
      '@typescript-eslint/no-empty-function': 'error',
      // code-style.md, rule 33: a choice of three or more is an `if` chain, a lookup record or an early return
      'no-nested-ternary': 'error',
      // code-style.md, rule 40: 140 characters for code and for a comment; a line that holds a literal passes
      'max-len': ['error', { code: 140, ignoreStrings: true, ignoreTemplateLiterals: true, ignoreRegExpLiterals: true }],
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
      // code-style.md, rule 16, at the level the rule sees: the kinds of members, not the order of fields among themselves
      '@typescript-eslint/member-ordering': [
        'error',
        {
          default: {
            memberTypes: [
              'signature',
              'call-signature',
              'static-field',
              'field',
              'static-initialization',
              'constructor',
              ['get', 'set'],
              'public-method',
              'protected-method',
              'private-method',
            ],
          },
        },
      ],
    },
  },
  {
    // No `any` in templates either (angular.md, rule 1)
    files: ['**/*.html'],
    rules: {
      '@angular-eslint/template/no-any': 'error',
    },
  },
];
