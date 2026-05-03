import { Component, OnInit, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { NgIf } from '@angular/common';
import { take } from 'rxjs/operators';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-email-capture',
  standalone: true,
  imports: [FormsModule, NgIf],
  templateUrl: './email-capture.component.html',
  styleUrl: './email-capture.component.css'
})
export class EmailCaptureComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);

  visitorId = '';
  sourceToken = '';
  redirectTarget = '';
  campaign = '';

  email = '';
  submitting = false;
  errorMsg = '';
  paramError = '';

  private pageEnteredMs = Date.now();
  private alreadyCaptured = false;
  private hintEmail: string | null = null;

  ngOnInit(): void {
    this.route.queryParamMap.pipe(take(1)).subscribe((p) => {
      void this.bootstrapFromParams(p.get('visitorId'), p.get('src'), p.get('redirect'), p.get('cmp'));
    });
  }

  submit(): void {
    this.errorMsg = '';
    const trimmed = this.email.trim();
    if (!trimmed.includes('@')) {
      this.errorMsg = 'Enter a valid email.';
      return;
    }

    if (
      this.alreadyCaptured &&
      this.hintEmail &&
      trimmed.toLowerCase() === this.hintEmail.toLowerCase()
    ) {
      this.submitting = true;
      const reqUrl = this.mergedDestinationRequestUrl(true);
      firstValueFrom(this.http.get<RedirectMergeResponse>(reqUrl))
        .then((merged) => {
          this.persistKnownEmail(trimmed);
          window.location.replace(merged.redirectUrl);
        })
        .catch(() => {
          this.submitting = false;
          this.errorMsg = 'Could not continue. Try again in a moment.';
        });
      return;
    }

    this.submitting = true;
    const dwellMs = Math.max(0, Date.now() - this.pageEnteredMs);
    const url = this.apiUrl('/capture-email');

    firstValueFrom(
      this.http.post<CaptureEmailResponse>(url, {
        visitorId: this.visitorId,
        email: trimmed,
        source: this.sourceToken,
        redirect: this.redirectTarget,
        campaign: this.campaign || null,
        dwellMs
      })
    )
      .then((res) => {
        this.persistKnownEmail(trimmed);
        window.location.replace(res.redirectUrl);
      })
      .catch(() => {
        this.submitting = false;
        this.errorMsg = 'Could not continue. Try again in a moment.';
      });
  }

  private persistKnownEmail(mail: string): void {
    if (typeof localStorage === 'undefined') {
      return;
    }
    try {
      localStorage.setItem(`leadScoring.prefill.${this.visitorId}`, mail);
      localStorage.setItem('leadScoring.lastVisitorId', this.visitorId);
    } catch {
      /* ignore quota */
    }
  }

  private async bootstrapFromParams(
    visitorId: string | null,
    src: string | null,
    redirect: string | null,
    cmp: string | null
  ): Promise<void> {
    this.pageEnteredMs = Date.now();
    this.paramError = '';

    if (!visitorId?.trim()) {
      this.paramError = 'This link is missing visitor information. Go back and use your campaign link.';
      return;
    }
    if (!redirect?.trim()) {
      this.paramError = 'This link is incomplete. Go back and use your campaign link.';
      return;
    }

    try {
      this.redirectTarget = decodeURIComponent(redirect);
    } catch {
      this.redirectTarget = redirect!;
    }

    this.visitorId = visitorId.trim();
    this.sourceToken = src?.trim() || 'unknown';
    this.campaign = cmp?.trim() || '';

    this.syncVisitorStorage(this.visitorId);

    const cached =
      typeof localStorage !== 'undefined'
        ? localStorage.getItem(`leadScoring.prefill.${this.visitorId}`)
        : null;
    if (cached?.trim()) {
      this.email = cached.trim();
    }

    let hint: EmailHint | null = null;
    try {
      const hintUrl = `${this.apiUrl('/track/email-hint')}?visitorId=${encodeURIComponent(this.visitorId)}`;
      hint = await firstValueFrom(this.http.get<EmailHint>(hintUrl));
    } catch {
      hint = null;
    }

    if (hint?.email?.trim()) {
      this.email = hint.email!.trim();
    }

    this.alreadyCaptured = hint?.alreadyCaptured ?? false;
    this.hintEmail = hint?.email?.trim() ? hint!.email!.trim() : null;
  }

  private syncVisitorStorage(vid: string): void {
    if (typeof localStorage === 'undefined') {
      return;
    }
    try {
      localStorage.setItem('leadScoring.lastVisitorId', vid);
    } catch {
      /* ignore */
    }
  }

  /** Build GET URL against merged-destination; API expects query-string params including full redirect URL. */
  private mergedDestinationRequestUrl(emailCaptured: boolean): string {
    const p = new URLSearchParams();
    p.set('redirect', this.redirectTarget);
    p.set('src', this.sourceToken);
    if (this.campaign) {
      p.set('cmp', this.campaign);
    }
    if (emailCaptured) {
      p.set('emailCaptured', 'true');
    }

    const base = `${this.apiUrl('/track/merged-destination')}`;
    return `${base}?${p.toString()}`;
  }

  private apiOrigin(): string {
    if (typeof window === 'undefined') {
      return 'http://localhost:5221';
    }

    const host = window.location.hostname;
    if (host === 'localhost' || host === '127.0.0.1') {
      return 'http://localhost:5221';
    }

    if (this.isPrivateIpv4Host(host)) {
      return `http://${host}:5221`;
    }

    return '';
  }

  private apiUrl(path: string): string {
    const origin = this.apiOrigin().replace(/\/$/, '');
    const p = path.startsWith('/') ? path : `/${path}`;
    return origin ? `${origin}${p}` : p;
  }

  private isPrivateIpv4Host(host: string): boolean {
    const parts = host.split('.').map((x) => Number(x));
    if (parts.length !== 4 || parts.some((x) => Number.isNaN(x) || x < 0 || x > 255)) {
      return false;
    }

    const [a, b] = parts;
    return a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168);
  }
}

interface CaptureEmailResponse {
  redirectUrl: string;
}

interface EmailHint {
  email?: string | null;
  alreadyCaptured: boolean;
}

interface RedirectMergeResponse {
  redirectUrl: string;
}
