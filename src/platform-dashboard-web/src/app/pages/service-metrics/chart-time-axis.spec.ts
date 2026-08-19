import { formatTickLabel, formatTooltipTimestamp, getAxisBounds } from './chart-time-axis';

describe('getAxisBounds', () => {
  const now = new Date('2026-08-15T14:36:00.000Z');

  it('computes min/max/unit/stepSize for 1h range', () => {
    const bounds = getAxisBounds('1h', now);
    expect(bounds.max).toBe(now.getTime());
    expect(bounds.min).toBe(now.getTime() - 60 * 60 * 1000);
    expect(bounds.unit).toBe('minute');
    expect(bounds.stepSize).toBe(10);
  });

  it('computes min/max/unit/stepSize for 24h range', () => {
    const bounds = getAxisBounds('24h', now);
    expect(bounds.max).toBe(now.getTime());
    expect(bounds.min).toBe(now.getTime() - 24 * 60 * 60 * 1000);
    expect(bounds.unit).toBe('hour');
    expect(bounds.stepSize).toBe(4);
  });

  it('computes min/max/unit/stepSize for 7d range', () => {
    const bounds = getAxisBounds('7d', now);
    expect(bounds.max).toBe(now.getTime());
    expect(bounds.min).toBe(now.getTime() - 7 * 24 * 60 * 60 * 1000);
    expect(bounds.unit).toBe('day');
    expect(bounds.stepSize).toBe(1);
  });
});

describe('formatTickLabel', () => {
  const date = new Date('2026-08-15T14:05:00.000Z');

  it('formats 1h range as HH:mm', () => {
    expect(formatTickLabel(date, '1h')).toMatch(/^\d{2}:\d{2}$/);
  });

  it('formats 24h range as HH:mm', () => {
    expect(formatTickLabel(date, '24h')).toMatch(/^\d{2}:\d{2}$/);
  });

  it('formats 7d range as short month + day', () => {
    expect(formatTickLabel(date, '7d')).toMatch(/^[A-Za-z]{3,}\s\d{1,2}$/);
  });
});

describe('formatTooltipTimestamp', () => {
  it('includes full date and time precision', () => {
    const date = new Date('2026-08-15T14:23:07.000Z');
    const formatted = formatTooltipTimestamp(date);
    expect(formatted).toContain('2026');
    expect(formatted).toMatch(/\d{2}:\d{2}:\d{2}/);
  });
});
