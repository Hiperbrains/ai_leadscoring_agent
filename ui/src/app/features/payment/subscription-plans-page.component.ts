import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../shared/services/auth.service';
import {
  ANNUAL_DISCOUNT_PERCENT,
  BillingInterval,
  PLAN_CATALOG,
  annualMonthlyEquivalent,
  formatUsd,
  getPlanCatalogEntry,
  normalizePlanName,
  planPriceDisplay
} from '../../shared/constants/plan-catalog';
import { PaymentPlanOption, PaymentService } from '../../shared/services/payment.service';

@Component({
  selector: 'app-subscription-plans-page',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './subscription-plans-page.component.html',
  styleUrl: './subscription-plans-page.component.scss'
})
export class SubscriptionPlansPageComponent implements OnInit {
  private readonly payment = inject(PaymentService);
  private readonly route = inject(ActivatedRoute);
  readonly auth = inject(AuthService);

  companyName = '';
  currentPlan = '';
  loginName = '';
  fromSignup = false;
  plans: PaymentPlanOption[] = [];
  selectedPlan = '';
  billingInterval: BillingInterval = 'monthly';
  annualDiscountPercent = ANNUAL_DISCOUNT_PERCENT;
  loading = true;
  submitting = false;
  error = '';
  persistPayments = false;

  ngOnInit(): void {
    const user = this.auth.user();
    this.loginName = user?.email ?? '';
    if (user?.companyName) {
      this.companyName = user.companyName;
    }
    if (user?.selectedPlan) {
      this.currentPlan = normalizePlanName(user.selectedPlan);
    }

    this.fromSignup = this.route.snapshot.queryParamMap.get('from') === 'signup';
    const preferredPlan = normalizePlanName(this.route.snapshot.queryParamMap.get('plan') ?? '');

    this.loadOptions(preferredPlan);
  }

  loadOptions(preferredPlan = ''): void {
    this.loading = true;
    this.error = '';
    this.payment.getOptions().subscribe({
      next: (res) => {
        this.companyName = res.companyName;
        this.currentPlan = normalizePlanName(res.currentPlan);
        this.annualDiscountPercent = res.annualDiscountPercent ?? ANNUAL_DISCOUNT_PERCENT;
        this.persistPayments = res.persistPayments ?? false;

        const apiByName = new Map(
          (res.plans ?? []).map((p) => [normalizePlanName(p.name), p] as const)
        );

        this.plans = PLAN_CATALOG.map((catalog) => {
          const api = apiByName.get(catalog.name);
          return {
            name: catalog.name,
            monthlyPrice: catalog.monthlyPrice,
            description: catalog.description,
            features: [...catalog.features],
            isCurrent: api?.isCurrent ?? this.currentPlan === catalog.name,
            isMostPopular: api?.isMostPopular ?? catalog.name === 'Growth',
            canCheckoutMonthly: api?.canCheckoutMonthly ?? false,
            canCheckoutAnnual: api?.canCheckoutAnnual ?? false
          };
        });

        const firstSelectable = this.plans.find((p) => !p.isCurrent && this.canCheckout(p));
        const preferred = preferredPlan
          ? this.plans.find((p) => p.name === preferredPlan && this.canCheckout(p))
          : undefined;
        this.selectedPlan = preferred?.name ?? firstSelectable?.name ?? preferredPlan ?? '';
        this.loading = false;
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 0) {
          this.error = 'Cannot reach the API. Start LeadScoring.Api on http://localhost:5221 and sign in again.';
        } else if (err.status === 401) {
          this.error = 'Session expired. Please sign in again.';
        } else {
          this.error = err.error?.message ?? `Could not load payment options (${err.status}).`;
        }
        this.persistPayments = false;
        this.plans = PLAN_CATALOG.map((catalog) => ({
          name: catalog.name,
          monthlyPrice: catalog.monthlyPrice,
          description: catalog.description,
          features: [...catalog.features],
          isCurrent: this.currentPlan === catalog.name,
          isMostPopular: catalog.name === 'Growth',
          canCheckoutMonthly: false,
          canCheckoutAnnual: false
        }));
        this.loading = false;
      }
    });
  }

  setBillingInterval(interval: BillingInterval): void {
    this.billingInterval = interval;
    this.error = '';
  }

  isSelected(plan: PaymentPlanOption): boolean {
    return this.selectedPlan === plan.name;
  }

  canCheckout(plan: PaymentPlanOption): boolean {
    if (plan.isCurrent || plan.monthlyPrice === null) {
      return false;
    }
    return plan.canCheckoutMonthly || plan.canCheckoutAnnual;
  }

  displayPrice(plan: PaymentPlanOption): string {
    const entry = getPlanCatalogEntry(plan.name);
    if (entry) {
      return planPriceDisplay(entry, this.billingInterval).amount;
    }
    if (plan.monthlyPrice === null) {
      return 'Custom';
    }
    if (this.billingInterval === 'monthly') {
      return `${formatUsd(plan.monthlyPrice)}/mo`;
    }
    return `${formatUsd(annualMonthlyEquivalent(plan.monthlyPrice, this.annualDiscountPercent))}/mo`;
  }

  billingNote(plan: PaymentPlanOption): string {
    const entry = getPlanCatalogEntry(plan.name);
    if (entry) {
      return planPriceDisplay(entry, this.billingInterval).billingNote;
    }
    if (plan.monthlyPrice === null) {
      return 'Volume pricing available';
    }
    return this.billingInterval === 'monthly'
      ? 'Billed monthly'
      : `Billed annually · Save ${this.annualDiscountPercent}%`;
  }

  choosePlan(plan: PaymentPlanOption): void {
    this.selectedPlan = plan.name;
    this.payWithStripe();
  }

  contactSales(): void {
    window.location.href = 'mailto:sales@hiperbrains.com?subject=Enterprise%20plan%20inquiry';
  }

  payWithStripe(): void {
    const plan = this.plans.find((p) => p.name === this.selectedPlan);
    if (!plan) {
      this.error = 'Please select a plan.';
      return;
    }
    if (plan.isCurrent) {
      this.error = 'You are already on this plan.';
      return;
    }
    if (plan.monthlyPrice === null) {
      this.error = 'Contact sales for Enterprise pricing.';
      return;
    }

    this.submitting = true;
    this.error = '';
    this.payment.createCheckoutSession(plan.name, this.billingInterval).subscribe({
      next: (res) => {
        if (res.checkoutUrl) {
          window.location.href = res.checkoutUrl;
          return;
        }
        this.error = 'Stripe did not return a checkout URL.';
        this.submitting = false;
      },
      error: (err: HttpErrorResponse) => {
        this.error = err.error?.message ?? 'Could not start Stripe checkout.';
        this.submitting = false;
      }
    });
  }
}
