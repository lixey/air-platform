export function boxesOf(parent: Element): string[] {
  return [...parent.children].map(child => {
    const tag = child.tagName.toLowerCase();
    if (tag !== 'div') {
      return tag;
    }
    const classes = [...child.classList];
    const head = classes.length > 0 ? `div.${classes.join('.')}` : 'div';
    return `${head}{${boxesOf(child).join(', ')}}`;
  });
}
