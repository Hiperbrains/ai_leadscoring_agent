import { PLAN_CATALOG, formatUsd, planPriceDisplay } from './plan-catalog';

export const PLAN_DETAILS: Record<string, { price: string; blurb: string }> = Object.fromEntries(
  PLAN_CATALOG.map((p) => {
    const display = planPriceDisplay(p, 'monthly');
    return [p.name, { price: display.amount, blurb: p.description }];
  })
);

export function signupPlanPrice(name: string): string {
  const entry = PLAN_CATALOG.find((p) => p.name === name);
  if (!entry || entry.monthlyPrice === null) {
    return 'Custom';
  }
  return `${formatUsd(entry.monthlyPrice)}/mo`;
}
