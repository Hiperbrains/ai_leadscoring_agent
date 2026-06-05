import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { BillingInterval, normalizePlanName } from '../../shared/constants/plan-catalog';
import { AuthService } from '../../shared/services/auth.service';
import { PaymentService, SubscriptionSummary } from '../../shared/services/payment.service';

@Component({
  selector: 'app-payment-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './payment-page.component.html',
  styleUrl: './payment-page.component.scss'
})
export class PaymentPageComponent implements OnInit {
  private readonly payment = inject(PaymentService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  summary: SubscriptionSummary | null = null;
  loading = true;
  confirmingCheckout = false;
  renewing = false;
  error = '';
  statusMessage = '';

  ngOnInit(): void {
    const status = this.route.snapshot.queryParamMap.get('status');
    const sessionId = this.route.snapshot.queryParamMap.get('session_id');

    if (status === 'success' && sessionId) {
      this.confirmCheckout(sessionId);
      return;
    }

    if (status === 'success') {
      this.statusMessage =
        'Payment completed in Stripe. If details are missing, complete checkout again or contact support.';
    } else if (status === 'cancelled') {
      this.statusMessage = 'Checkout was cancelled. You can renew or change your plan anytime.';
    }

    this.loadSummary();
  }

  private confirmCheckout(sessionId: string): void {
    this.confirmingCheckout = true;
    this.loading = true;
    this.error = '';
    this.payment.confirmCheckout(sessionId).subscribe({
      next: (summary) => {
        this.summary = summary;
        this.loading = false;
        this.confirmingCheckout = false;
        this.statusMessage = 'Payment completed successfully. Your subscription details are updated below.';
        this.auth.refreshCurrentUser().subscribe();
        void this.router.navigate([], {
          relativeTo: this.route,
          queryParams: { status: 'success' },
          replaceUrl: true
        });
      },
      error: (err: HttpErrorResponse) => {
        this.confirmingCheckout = false;
        this.error = err.error?.message ?? 'Could not confirm payment with the server.';
        this.statusMessage = 'Stripe checkout succeeded but saving to your account failed.';
        this.loadSummary();
      }
    });
  }

  loadSummary(): void {
    this.loading = true;
    this.error = '';
    this.payment.getSubscription().subscribe({
      next: (res) => {
        this.summary = res;
        this.loading = false;
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 0) {
          this.error = 'Cannot reach the API. Start LeadScoring.Api on http://localhost:5221 and sign in again.';
        } else if (err.status === 401) {
          this.error = 'Session expired. Please sign in again.';
        } else {
          this.error = err.error?.message ?? `Could not load payment details (${err.status}).`;
        }
        this.loading = false;
      }
    });
  }

  changeSubscription(): void {
    void this.router.navigate(['/settings/payment/subcriptionsplans']);
  }

  renewPlan(): void {
    if (!this.summary) {
      return;
    }

    if (this.summary.currentPlan === 'Enterprise') {
      window.location.href = 'mailto:sales@hiperbrains.com?subject=Enterprise%20plan%20renewal';
      return;
    }

    const billingInterval: BillingInterval =
      this.summary.billingType?.toLowerCase() === 'annual' ? 'annual' : 'monthly';

    this.renewing = true;
    this.error = '';
    this.payment.createCheckoutSession(this.summary.currentPlan, billingInterval, true).subscribe({
      next: (res) => {
        if (res.checkoutUrl) {
          window.location.href = res.checkoutUrl;
          return;
        }
        this.error = 'Stripe did not return a checkout URL.';
        this.renewing = false;
      },
      error: (err: HttpErrorResponse) => {
        this.error = err.error?.message ?? 'Could not start renewal checkout.';
        this.renewing = false;
      }
    });
  }

  formatAmount(cents: number | null | undefined, currency = 'usd'): string {
    if (cents == null) {
      return '—';
    }
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: currency.toUpperCase()
    }).format(cents / 100);
  }

  formatDate(value: string | null | undefined): string {
    if (!value) {
      return '—';
    }
    return new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    }).format(new Date(value));
  }

  creditPercentUsed(): number {
    if (!this.summary || this.summary.projectCreditsTotal <= 0) {
      return 0;
    }
    return Math.min(
      100,
      Math.round((this.summary.projectCreditsUsed / this.summary.projectCreditsTotal) * 100)
    );
  }

  displayPlan(plan: string): string {
    return normalizePlanName(plan);
  }
}
