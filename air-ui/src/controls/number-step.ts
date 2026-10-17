/** The step of a number box that names none. */
const DEFAULT_STEP = 1;

/**
 * The number a press of a number box's stepper writes: on an empty box `min`, else 0; on any other the value less or
 * plus the step, 1 when there is none, rounded to the step's decimals as the step is written (`0.1` one, `1` none,
 * `1e-7` seven); either held within `min` and `max`.
 */
export function steppedNumber(
  value: number | null,
  direction: 1 | -1,
  step: number | null | undefined,
  min: number | undefined,
  max: number | undefined,
): number {
  const by = step && step > 0 ? step : DEFAULT_STEP;
  const next = value === null ? (min ?? 0) : Number((value + direction * by).toFixed(decimalsOf(by)));
  return heldWithin(next, min, max);
}

/** Whether a press of the stepper toward `direction` changes the value: false at or beyond its bound, true on an empty box. */
export function canStep(value: number | null, direction: 1 | -1, min: number | undefined, max: number | undefined): boolean {
  if (value === null) {
    return true;
  }
  const bound = direction === 1 ? max : min;
  return bound === undefined || (direction === 1 ? value < bound : value > bound);
}

/** The decimals a step is written with: the digits after its point, more by a negative exponent. */
function decimalsOf(step: number): number {
  const [mantissa, exponent] = String(step).toLowerCase().split('e');
  const fraction = mantissa.split('.')[1]?.length ?? 0;
  return Math.min(100, Math.max(0, fraction - Number(exponent ?? 0)));
}

function heldWithin(value: number, min: number | undefined, max: number | undefined): number {
  const above = min === undefined ? value : Math.max(min, value);
  return max === undefined ? above : Math.min(max, above);
}
