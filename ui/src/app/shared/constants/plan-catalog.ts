export type BillingInterval = 'monthly' | 'annual';

export const ANNUAL_DISCOUNT_PERCENT = 11;

export interface PlanCatalogEntry {
  name: string;
  monthlyPrice: number | null;
  description: string;
  features: string[];
}

export const PLAN_CATALOG: PlanCatalogEntry[] = [
  {
    name: 'Starter',
    monthlyPrice: 99,
    description: 'Everything you need to start discovering and contacting your first prospects.',
    features: [
      'Up to 1,000 leads / month',
      '1 active ICP profile',
      'Email campaign automation',
      'Basic engagement tracking',
      'Full engagement intelligence',
      'AI lead scoring + priority queue',
      'Salesforce & HubSpot integration',
      'Revenue forecasting dashboard'
    ]
  },
  {
    name: 'Growth',
    monthlyPrice: 199,
    description: 'The full autonomous growth engine. Ideal for sales teams ready to scale pipeline.',
    features: [
      'Up to 5,000 leads / month',
      'Unlimited ICP profiles',
      'Multi-channel campaigns (Email + LinkedIn)',
      'Full engagement intelligence',
      'AI lead scoring + priority queue',
      'Salesforce & HubSpot integration',
      'Revenue forecasting dashboard'
    ]
  },
  {
    name: 'Enterprise',
    monthlyPrice: null,
    description:
      'Dedicated infrastructure, custom AI models, and white-glove onboarding for large teams.',
    features: [
      'Unlimited leads & seats',
      'Dedicated AI models per ICP',
      'Custom data integrations & APIs',
      'SSO, SCIM + advanced security',
      '99.9% SLA + priority support',
      'Dedicated customer success manager',
      'SOC 2 Type II, GDPR, HIPAA'
    ]
  }
];

export function annualMonthlyEquivalent(
  monthlyPrice: number,
  discountPercent: number = ANNUAL_DISCOUNT_PERCENT
): number {
  return Math.round(monthlyPrice * (1 - discountPercent / 100));
}

export function formatUsd(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0
  }).format(amount);
}

export function planPriceDisplay(
  entry: PlanCatalogEntry,
  interval: BillingInterval
): { amount: string; billingNote: string } {
  if (entry.monthlyPrice === null) {
    return { amount: 'Custom', billingNote: 'Volume pricing available' };
  }

  if (interval === 'monthly') {
    return {
      amount: `${formatUsd(entry.monthlyPrice)}/mo`,
      billingNote: 'Billed monthly'
    };
  }

  const perMonth = annualMonthlyEquivalent(entry.monthlyPrice);
  return {
    amount: `${formatUsd(perMonth)}/mo`,
    billingNote: `Billed annually · Save ${ANNUAL_DISCOUNT_PERCENT}%`
  };
}

/** Maps legacy plan names stored on tenants to the current catalog. */
export function normalizePlanName(name: string): string {
  if (name === 'Professional') {
    return 'Growth';
  }
  return name;
}

export function getPlanCatalogEntry(name: string): PlanCatalogEntry | undefined {
  return PLAN_CATALOG.find((p) => p.name === normalizePlanName(name));
}
