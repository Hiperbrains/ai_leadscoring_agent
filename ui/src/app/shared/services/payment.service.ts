import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { apiUrl } from '../helpers/api-base.helper';
import { resolveAppOrigin } from '../helpers/public-url.helper';
import type { BillingInterval } from '../constants/plan-catalog';

export interface PaymentPlanOption {
  name: string;
  monthlyPrice: number | null;
  description: string;
  features: string[];
  isCurrent: boolean;
  isMostPopular: boolean;
  canCheckoutMonthly: boolean;
  canCheckoutAnnual: boolean;
}

export interface PaymentOptions {
  companyName: string;
  currentPlan: string;
  annualDiscountPercent: number;
  persistPayments: boolean;
  plans: PaymentPlanOption[];
}

export interface ProductCreditUsage {
  productId: number;
  productName: string;
  creditsUsed: number;
}

export interface PaymentHistoryItem {
  paymentId: string;
  amount: number;
  currency: string;
  paymentStatus: string;
  paymentDate: string;
  stripeSessionId: string | null;
}

export interface SubscriptionSummary {
  companyName: string;
  currentPlan: string;
  billingType: string | null;
  startDate: string | null;
  renewDate: string | null;
  expireDate: string | null;
  lastPaymentAmount: number | null;
  paymentStatus: string | null;
  projectCreditsTotal: number;
  projectCreditsUsed: number;
  projectCreditsRemaining: number;
  isActive: boolean;
  productCreditUsage: ProductCreditUsage[];
  recentPayments: PaymentHistoryItem[];
}

@Injectable({ providedIn: 'root' })
export class PaymentService {
  private readonly http = inject(HttpClient);

  getOptions(): Observable<PaymentOptions> {
    return this.http.get<PaymentOptions>(apiUrl('/api/payment/options'));
  }

  getSubscription(): Observable<SubscriptionSummary> {
    return this.http.get<SubscriptionSummary>(apiUrl('/api/payment/subscription'));
  }

  createCheckoutSession(
    plan: string,
    billingInterval: BillingInterval,
    isRenewal = false
  ): Observable<{ checkoutUrl: string }> {
    return this.http.post<{ checkoutUrl: string }>(apiUrl('/api/payment/checkout-session'), {
      plan,
      billingInterval,
      returnBaseUrl: resolveAppOrigin(),
      isRenewal
    });
  }

  confirmCheckout(sessionId: string): Observable<SubscriptionSummary> {
    return this.http.post<SubscriptionSummary>(apiUrl('/api/payment/confirm-checkout'), {
      sessionId
    });
  }
}
