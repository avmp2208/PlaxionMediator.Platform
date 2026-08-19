import { TimeRange } from '../../core/models';

const HOUR_MS = 60 * 60 * 1000;
const DAY_MS = 24 * HOUR_MS;

/** Duration of each selectable time range, in milliseconds. */
const RANGE_DURATION_MS: Record<TimeRange, number> = {
  '1h': HOUR_MS,
  '24h': DAY_MS,
  '7d': 7 * DAY_MS
};

export interface AxisBounds {
  min: number;
  max: number;
  unit: 'minute' | 'hour' | 'day';
  stepSize: number;
  maxTicksLimit: number;
}

/**
 * Computes the fixed, full-range X-axis domain for a given range/`now`.
 *
 * `now` must be computed exactly once per data-load in the component and passed in here for every
 * chart on the page, so that all charts share an identical axis domain instead of each independently
 * calling `Date.now()` at slightly different times.
 */
export function getAxisBounds(range: TimeRange, now: Date): AxisBounds {
  const nowMs = now.getTime();
  const min = nowMs - RANGE_DURATION_MS[range];

  switch (range) {
    case '1h':
      return { min, max: nowMs, unit: 'minute', stepSize: 10, maxTicksLimit: 7 };
    case '24h':
      return { min, max: nowMs, unit: 'hour', stepSize: 4, maxTicksLimit: 7 };
    case '7d':
      return { min, max: nowMs, unit: 'day', stepSize: 1, maxTicksLimit: 8 };
  }
}

/**
 * The fixed [start, end] window for a given range/`now` - `end` is always `now` itself, so the
 * selected range keeps extending all the way to the current time rather than stopping at the
 * timestamp of the last received metric.
 */
export function getRangeBounds(range: TimeRange, now: Date): { start: Date; end: Date } {
  return { start: new Date(now.getTime() - RANGE_DURATION_MS[range]), end: now };
}

/** Compact axis tick label for a given range - full precision is reserved for tooltips. */
export function formatTickLabel(date: Date, range: TimeRange): string {
  switch (range) {
    case '1h':
    case '24h':
      return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false });
    case '7d':
      return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }
}

/** Full-precision timestamp used in tooltips, regardless of the selected range. */
export function formatTooltipTimestamp(date: Date): string {
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  });
}
