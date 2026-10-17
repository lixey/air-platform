import type { FormElementDto } from '../core/generated/contracts';

/**
 * The columns of the form's twelve an element takes, folding with the panel's width:
 * the whole row when narrow, then half of it or all of it, its own span from `@7xl`.
 */
const SPAN_CLASSES: Readonly<Record<number, string>> & { readonly 0: string } = {
  0: 'col-span-full @4xl:col-span-6 @7xl:col-span-3',
  1: 'col-span-full @7xl:col-span-1',
  2: 'col-span-full @4xl:col-span-6 @7xl:col-span-2',
  3: 'col-span-full @4xl:col-span-6 @7xl:col-span-3',
  4: 'col-span-full @4xl:col-span-6 @7xl:col-span-4',
  5: 'col-span-full @6xl:col-span-12 @7xl:col-span-5',
  6: 'col-span-full @4xl:col-span-6',
  7: 'col-span-full @6xl:col-span-12 @7xl:col-span-7',
  8: 'col-span-full @6xl:col-span-12 @7xl:col-span-8',
  9: 'col-span-full @6xl:col-span-12 @7xl:col-span-9',
  10: 'col-span-full @6xl:col-span-12 @7xl:col-span-10',
  11: 'col-span-full @6xl:col-span-12 @7xl:col-span-11',
  12: 'col-span-full @6xl:col-span-12 @7xl:col-span-12',
};

/** The classes of the columns `element` takes in the form's twelve; an unknown span is the default one. */
export function spanClassOf(element: FormElementDto): string {
  return SPAN_CLASSES[element.span] ?? SPAN_CLASSES[0];
}
