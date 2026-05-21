import { Component, HostListener, OnDestroy, OnInit, inject } from '@angular/core';
import { DecimalPipe, DatePipe, NgFor, NgIf } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter, finalize } from 'rxjs';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { AppBadgeComponent } from '../shared/components/badge/app-badge.component';
import { AppButtonComponent } from '../shared/components/button/app-button.component';
import { AppCardComponent } from '../shared/components/card/app-card.component';
import { AppComboboxComponent } from '../shared/components/combobox/app-combobox.component';
import { AppInputComponent } from '../shared/components/input/app-input.component';
import { AppTableComponent } from '../shared/components/table/app-table.component';
import { EventsBarChartComponent } from '../shared/components/dashboard-charts/events-bar-chart.component';
import { SourceBarChartComponent } from '../shared/components/dashboard-charts/source-bar-chart.component';
import { StagePieChartComponent } from '../shared/components/dashboard-charts/stage-pie-chart.component';
import { WorkspaceTopBarComponent } from './workspace-top-bar/workspace-top-bar.component';
import { WEBSITE_EMBED_SCRIPT } from '../shared/constants/website-embed-script';
import { AuthService } from '../shared/services/auth.service';

@Component({
  selector: 'app-workspace',
  standalone: true,
  imports: [
    DecimalPipe,
    DatePipe,
    NgIf,
    NgFor,
    FormsModule,
    AppBadgeComponent,
    AppButtonComponent,
    AppCardComponent,
    AppComboboxComponent,
    AppInputComponent,
    AppTableComponent,
    EventsBarChartComponent,
    SourceBarChartComponent,
    StagePieChartComponent,
    WorkspaceTopBarComponent
  ],
  templateUrl: './workspace.component.html',
  styleUrl: './workspace.component.scss'
})
export class WorkspaceComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly auth = inject(AuthService);
  private copyFlashTimer?: ReturnType<typeof setTimeout>;
  /** Debounced reload for company-config list search (ms). */
  private static readonly companyConfigFilterDebounceMs = 320;
  /** Debounced autofill after company/product typing so we sync only when typing pauses (combobox emits every keystroke). */
  private static readonly mainCompanyProductHydrateDebounceMs = 350;
  /** Default Warm/MQL/Hot minimum-score boundaries (aligned with backend legacy buckets). */
  private static readonly defaultStageFormValues = {
    warmMinScore: '51',
    mqlMinScore: '101',
    hotMinScore: '151'
  } as const;
  private companyConfigFilterSearchTimer?: ReturnType<typeof setTimeout>;
  /** Set after the first `syncTabFromRoute` applies so identical tab route events can be skipped. */
  private workspaceRouteSynced = false;
  apiBase = this.resolveApiBase();
  error = '';
  /** KPIs and charts only (GET /api/dashboard/summary). */
  dashboardSummary?: DashboardSummaryResponse;
  /** Full lead rows for the Leads table (GET /api/dashboard/leads). */
  dashboardLeads: DashboardLead[] = [];
  /** Set after the first successful leads fetch (even when the list is empty). */
  dashboardLeadsLoaded = false;
  loadingSummary = false;
  loadingLeads = false;
  selectedFile?: File;
  source = '';
  importMessage = '';
  importError = '';
  importing = false;
  pageSize = 10;
  currentPage = 1;
  /** Empty string = all sources; matches `lastEventSource` from the API (or missing/Unknown when set to Unknown). */
  leadsSourceFilter = '';
  /** Empty string = all campaigns; use `leadsCampaignNoneSentinel` for rows with no campaign. */
  leadsCampaignFilter = '';
  /** Empty string = all stages. */
  leadsStageFilter: StageName | '' = '';
  /** Lead signup filter for list view. */
  leadsSignupFilter: 'all' | 'signed-up' | 'not-signed-up' = 'all';
  readonly leadsCampaignNoneSentinel = '__no_campaign__';
  readonly knownEventSourceLabels = ['Unknown', 'Email', 'Website', 'LinkedIn', 'Direct', 'Organic'] as const;
  activeTab: LeftTab = 'dashboard';
  /** Labels for Universal URL `src`; stored value matches option text; URL uses lowercase. */
  readonly trackingSourceOptions = ['Email', 'LinkedIn', 'Twitter', 'Facebook', 'Google', 'Other'];
  universalLinkSource = '';
  universalLinkCampaign = '';
  universalLinkRedirect = '';
  trackingLinkCopyStatus = '';
  linkCopiedFlash = false;
  readonly websiteEmbedScript = WEBSITE_EMBED_SCRIPT;
  websiteScriptCopyStatus = '';
  websiteScriptCopiedFlash = false;
  companyNameFilter = '';
  companyConfigs: CompanyProductConfig[] = [];
  /** Unfiltered list for combobox suggestions (unaffected by table filter). */
  companyProductAll: CompanyProductConfig[] = [];
  /** Debounced autofill timer for Product Event rows + thresholds when picking an existing company/product pair. */
  private mainFormHydrateTimer?: ReturnType<typeof setTimeout>;
  configLoading = false;
  configError = '';
  configSuccess = '';
  savingConfig = false;
  deletingConfigId: string | null = null;
  /** When set, the delete confirmation modal is visible. */
  companyConfigDeleteTarget: CompanyProductConfig | null = null;
  /** When set, the edit-company-config modal is open (matches row id). */
  companyConfigEditModalId: string | null = null;
  companyProductEditForm: CompanyProductForm = {
    companyName: '',
    productName: '',
    items: [{ eventName: '', score: '' }],
    warmMinScore: WorkspaceComponent.defaultStageFormValues.warmMinScore,
    mqlMinScore: WorkspaceComponent.defaultStageFormValues.mqlMinScore,
    hotMinScore: WorkspaceComponent.defaultStageFormValues.hotMinScore
  };
  configEditModalError = '';
  savingEditModal = false;
  companyProductForm: CompanyProductForm = {
    companyName: '',
    productName: '',
    items: [{ eventName: '', score: '' }],
    warmMinScore: WorkspaceComponent.defaultStageFormValues.warmMinScore,
    mqlMinScore: WorkspaceComponent.defaultStageFormValues.mqlMinScore,
    hotMinScore: WorkspaceComponent.defaultStageFormValues.hotMinScore
  };
  manualBatchType: ManualBatchType = 'Day1';
  private _manualScope: ManualScope = 'TotalEligible';
  manualLeadTakeCount = 1;
  private pendingResetTakeToFullBucket = false;
  manualPreview?: BatchPreviewResult;
  manualRunResult?: BatchManualRunResult;
  manualRunHistory: ManualRunHistoryItem[] = [];
  /** Persisted rows from `BatchLogs` (daily + manual runs). */
  batchLogHistoryRows: BatchLogHistoryRow[] = [];
  readonly batchLogHistoryPageSize = 10;
  batchLogHistoryPage = 1;
  batchLogHistoryLoading = false;
  batchLogHistoryError = '';
  manualRunJobId?: string;
  manualRunStatus?: BatchManualRunStatus;
  private manualRunPollTimer?: ReturnType<typeof setInterval>;
  manualLoading = false;
  manualError = '';

  /** Template type codes for Configure send (order matches UX labels). */
  readonly manualBatchTemplateOptions: ManualBatchType[] = [
    'Day1',
    'Warm',
    'Mql',
    'Hot',
    'Day2',
    'WarmFollowUp',
    'MqlFollowUp',
    'HotFollowUp'
  ];

  /** Spinner state: which slice is loading depends on the active tab. */
  get loading(): boolean {
    if (this.activeTab === 'dashboard') {
      return this.loadingSummary;
    }
    if (this.activeTab === 'leads') {
      return this.loadingLeads;
    }
    return false;
  }

  /** Merges summary + leads for templates, filters, and exports. */
  get data(): DashboardResponse | undefined {
    if (!this.dashboardSummary && !this.dashboardLeadsLoaded) {
      return undefined;
    }
    const s = this.dashboardSummary;
    return {
      totalLeads: s?.totalLeads ?? this.dashboardLeads.length,
      signedUpCount: s?.signedUpCount ?? 0,
      stageCounts: s?.stageCounts ?? { Cold: 0, Warm: 0, Mql: 0, Hot: 0 },
      eventsByType: s?.eventsByType ?? { Open: 0, EmailClick: 0, WebsiteActivity: 0 },
      firstSourceCounts: s?.firstSourceCounts ?? {},
      leads: this.dashboardLeads
    };
  }

  /** Selected lead bucket for Manual Batch. Changing scope resets leads-to-process to the full bucket size. */
  get manualScope(): ManualScope {
    return this._manualScope;
  }

  set manualScope(value: ManualScope) {
    if (this._manualScope === value) {
      return;
    }
    this._manualScope = value;
    const n = this.manualSelectedBucketCount;
    this.manualLeadTakeCount = n > 0 ? n : 1;
  }

  /** Count of leads in the currently selected bucket (from last preview). */
  get manualSelectedBucketCount(): number {
    const p = this.manualPreview;
    if (!p) {
      return 0;
    }
    switch (this.manualScope) {
      case 'TotalEligible':
        return p.totalEligibleCount;
      case 'TotalLeads':
        return p.totalLeadsCount;
      case 'Stage0':
        return p.stage0Count;
      case 'Stage1':
        return p.stage1Count;
      case 'Stage2':
        return p.stage2Count;
      case 'Stage3':
        return p.stage3Count;
      case 'Stage4':
        return p.stage4Count;
      case 'NewLeads':
        return p.newLeadsCount;
      case 'Last2DaysInactive':
        return p.last2DaysInactiveCount;
      case 'Last4DaysSinceEmail':
        return p.last4DaysSinceLastEmailCount;
      case 'DidNotOpenEmail':
        return p.didNotOpenEmailCount;
      default:
        return 0;
    }
  }

  get canRunManualBatch(): boolean {
    return (
      !!this.manualPreview &&
      !this.manualLoading &&
      this.manualSelectedBucketCount > 0 &&
      this.manualLeadTakeCount >= 1
    );
  }

  /** Unique company names from saved configs (datalist suggestions). */
  get distinctCompanyNames(): string[] {
    const set = new Set<string>();
    for (const c of this.companyProductAll) {
      const n = c.companyName?.trim();
      if (n) {
        set.add(n);
      }
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }

  /** Product names for the typed/selected company (trimmed names, case-insensitive). */
  get productsForSelectedCompany(): string[] {
    return this.productNamesForCompany(this.companyProductForm.companyName);
  }

  /** Product suggestions for the edit modal combobox. */
  get productsForEditModalCompany(): string[] {
    return this.productNamesForCompany(this.companyProductEditForm.companyName);
  }

  private productNamesForCompany(companyNameRaw: string): string[] {
    const target = companyNameRaw.trim().toLowerCase();
    if (!target) {
      return [];
    }
    const set = new Set<string>();
    for (const c of this.companyProductAll) {
      if (c.companyName.trim().toLowerCase() === target) {
        const p = c.productName?.trim();
        if (p) {
          set.add(p);
        }
      }
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }

  /** Combobox emits on every keystroke; debounce so we only hydrate after the user settles on names. */
  scheduleMainCompanyProductHydrate(): void {
    if (this.mainFormHydrateTimer !== undefined) {
      clearTimeout(this.mainFormHydrateTimer);
    }
    this.mainFormHydrateTimer = setTimeout(() => {
      this.mainFormHydrateTimer = undefined;
      this.hydrateMainCompanyProductFormFromSavedConfigs();
    }, WorkspaceComponent.mainCompanyProductHydrateDebounceMs);
  }

  private clearMainFormHydrateTimer(): void {
    if (this.mainFormHydrateTimer !== undefined) {
      clearTimeout(this.mainFormHydrateTimer);
      this.mainFormHydrateTimer = undefined;
    }
  }

  private hydrateMainCompanyProductFormFromSavedConfigs(): void {
    const cn = this.companyProductForm.companyName.trim();
    const pn = this.companyProductForm.productName.trim();
    if (!cn || !pn) {
      return;
    }

    const match = this.findSavedCompanyProductPair(cn, pn);
    if (!match) {
      return;
    }

    this.companyProductForm = this.companyProductFormSnapshotFromSaved(
      match,
      this.companyProductForm.companyName,
      this.companyProductForm.productName
    );
  }

  private findSavedCompanyProductPair(cnTrimmed: string, pnTrimmed: string): CompanyProductConfig | undefined {
    return this.companyProductAll.find(
      (c) =>
        c.companyName.trim().toLowerCase() === cnTrimmed.toLowerCase() &&
        c.productName.trim().toLowerCase() === pnTrimmed.toLowerCase()
    );
  }

  private companyProductFormSnapshotFromSaved(
    config: CompanyProductConfig,
    companyNamePreserve: string,
    productNamePreserve: string
  ): CompanyProductForm {
    const mapped = this.eventConfigEntries(config.productEventConfig).map((x) => ({
      eventName: x.key,
      score: x.value
    }));
    const st = config.stageThresholds;
    return {
      companyName: companyNamePreserve,
      productName: productNamePreserve,
      items: mapped.length > 0 ? mapped : [{ eventName: '', score: '' }],
      warmMinScore: String(st?.warmMin ?? WorkspaceComponent.defaultStageFormValues.warmMinScore),
      mqlMinScore: String(st?.mqlMin ?? WorkspaceComponent.defaultStageFormValues.mqlMinScore),
      hotMinScore: String(st?.hotMin ?? WorkspaceComponent.defaultStageFormValues.hotMinScore)
    };
  }

  ngOnInit(): void {
    this.syncTabFromRoute();
    this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd)).subscribe(() => this.syncTabFromRoute());
  }

  /** Maps API stage enums to badge token keys (CSS classes). */
  stageBadge(stage: StageName): 'cold' | 'warm' | 'mql' | 'hot' {
    switch (stage) {
      case 'Cold':
        return 'cold';
      case 'Warm':
        return 'warm';
      case 'Mql':
        return 'mql';
      case 'Hot':
        return 'hot';
      default:
        return 'cold';
    }
  }

  viewLeadDetail(leadId: string): void {
    void this.router.navigate(['/leads', leadId]);
  }

  openLeadsFromKpi(kpi: 'total' | 'cold' | 'warm' | 'mql' | 'hot' | 'signed-up'): void {
    this.leadsSourceFilter = '';
    this.leadsCampaignFilter = '';
    this.currentPage = 1;
    this.leadsStageFilter = '';
    this.leadsSignupFilter = 'all';

    switch (kpi) {
      case 'cold':
        this.leadsStageFilter = 'Cold';
        break;
      case 'warm':
        this.leadsStageFilter = 'Warm';
        break;
      case 'mql':
        this.leadsStageFilter = 'Mql';
        break;
      case 'hot':
        this.leadsStageFilter = 'Hot';
        break;
      case 'signed-up':
        this.leadsSignupFilter = 'signed-up';
        break;
      default:
        break;
    }

    void this.router.navigate(['/leads']);
  }

  private syncTabFromRoute(): void {
    let r: ActivatedRoute | null = this.route;
    while (r?.firstChild) {
      r = r.firstChild;
    }
    const tab = r?.snapshot.data['workspaceTab'] as LeftTab | undefined;
    if (!tab) {
      return;
    }
    const duplicateNavSameTab = tab === this.activeTab && this.workspaceRouteSynced;
    if (duplicateNavSameTab) {
      return;
    }
    this.workspaceRouteSynced = true;

    if (this.activeTab === 'company-config' && tab !== 'company-config') {
      this.companyConfigDeleteTarget = null;
      this.closeCompanyConfigEditModal();
      this.clearMainFormHydrateTimer();
    }
    this.activeTab = tab;
    if (tab === 'dashboard' && !this.dashboardSummary && !this.loadingSummary) {
      this.loadDashboardSummary();
    }
    if (tab === 'leads' && !this.dashboardLeadsLoaded && !this.loadingLeads) {
      this.loadDashboardLeads();
    }
    if (tab === 'company-config') {
      this.reloadCompanyProductViews();
    }
    if (tab === 'manual-batch' && !this.manualLoading) {
      this.previewManualBatch();
      this.loadBatchLogHistory();
    }
  }

  ngOnDestroy(): void {
    this.stopManualRunPolling();
    this.clearMainFormHydrateTimer();
    if (this.companyConfigFilterSearchTimer !== undefined) {
      clearTimeout(this.companyConfigFilterSearchTimer);
      this.companyConfigFilterSearchTimer = undefined;
    }
    if (this.copyFlashTimer !== undefined) {
      clearTimeout(this.copyFlashTimer);
    }
  }

  get builtUniversalTrackingLink(): string {
    const base = this.universalLinkApiBase().replace(/\/$/, '');
    if (!base) {
      return '';
    }

    const params = new URLSearchParams();
    const src = this.universalLinkSource.trim().toLowerCase();
    if (src) {
      params.set('src', src);
    }
    const cmp = this.universalLinkCampaign.trim();
    if (cmp) {
      params.set('cmp', cmp);
    }

    const redirect = this.universalLinkRedirect.trim();
    if (redirect) {
      params.set('redirect', redirect);
    }

    const q = params.toString();
    return q ? `${base}/r?${q}` : `${base}/r`;
  }

  /** Empty uses API default redirect; invalid blocks copy/test. */
  get universalRedirectValidation(): 'ok' | 'empty' | 'invalid' {
    const r = this.universalLinkRedirect.trim();
    if (!r) {
      return 'empty';
    }
    try {
      const u = new URL(r);
      if (u.protocol !== 'http:' && u.protocol !== 'https:') {
        return 'invalid';
      }
      return 'ok';
    } catch {
      return 'invalid';
    }
  }

  get canCopyOrTestUniversalLink(): boolean {
    const url = this.builtUniversalTrackingLink;
    return !!url && this.universalRedirectValidation !== 'invalid';
  }

  async copyUniversalTrackingLink(): Promise<void> {
    this.trackingLinkCopyStatus = '';
    this.linkCopiedFlash = false;
    clearTimeout(this.copyFlashTimer);
    const url = this.builtUniversalTrackingLink;
    if (!url) {
      this.trackingLinkCopyStatus = 'Could not determine the API base URL for this page.';
      return;
    }
    if (this.universalRedirectValidation === 'invalid') {
      this.trackingLinkCopyStatus = 'Fix the redirect URL before copying.';
      return;
    }

    const flashCopied = (): void => {
      this.linkCopiedFlash = true;
      this.copyFlashTimer = setTimeout(() => (this.linkCopiedFlash = false), 2000);
    };

    try {
      await navigator.clipboard.writeText(url);
      flashCopied();
      return;
    } catch {
      /* fallback below */
    }

    const ta = document.getElementById('builtUniversalLink') as HTMLTextAreaElement | null;
    if (ta) {
      ta.focus();
      ta.select();
      ta.setSelectionRange(0, ta.value.length);
      try {
        const legacyOk = document.execCommand('copy');
        if (legacyOk) {
          flashCopied();
          return;
        }
        this.trackingLinkCopyStatus = 'Select the link above and press Ctrl+C.';
      } catch {
        this.trackingLinkCopyStatus = 'Select the link above and press Ctrl+C.';
      }
      return;
    }

    this.trackingLinkCopyStatus = 'Could not copy automatically.';
  }

  openUniversalTrackingLinkTest(): void {
    const url = this.builtUniversalTrackingLink;
    if (!url || this.universalRedirectValidation === 'invalid') {
      return;
    }
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  async copyWebsiteEmbedScript(): Promise<void> {
    this.websiteScriptCopyStatus = '';
    this.websiteScriptCopiedFlash = false;
    clearTimeout(this.copyFlashTimer);

    const flashCopied = (): void => {
      this.websiteScriptCopiedFlash = true;
      this.copyFlashTimer = setTimeout(() => (this.websiteScriptCopiedFlash = false), 2000);
    };

    try {
      await navigator.clipboard.writeText(this.websiteEmbedScript);
      flashCopied();
      return;
    } catch {
      /* fallback below */
    }

    const ta = document.getElementById('websiteEmbedScript') as HTMLTextAreaElement | null;
    if (ta) {
      ta.focus();
      ta.select();
      ta.setSelectionRange(0, ta.value.length);
      try {
        const legacyOk = document.execCommand('copy');
        if (legacyOk) {
          flashCopied();
          return;
        }
        this.websiteScriptCopyStatus = 'Select the script above and press Ctrl+C.';
      } catch {
        this.websiteScriptCopyStatus = 'Select the script above and press Ctrl+C.';
      }
      return;
    }

    this.websiteScriptCopyStatus = 'Could not copy automatically.';
  }

  loadDashboard(): void {
    if (this.activeTab === 'dashboard') {
      this.loadDashboardSummary();
    } else if (this.activeTab === 'leads') {
      this.loadDashboardLeads();
    } else {
      this.loadDashboardSummary();
      this.loadDashboardLeads();
    }
  }

  /** KPIs and charts only — used on the Dashboard tab. */
  loadDashboardSummary(): void {
    if (this.loadingSummary) {
      return;
    }
    this.loadingSummary = true;
    this.error = '';
    this.http.get<DashboardSummaryResponse>(`${this.apiBase}/api/dashboard/summary`).subscribe({
      next: (value) => {
        this.dashboardSummary = value;
        this.currentPage = 1;
        this.loadingSummary = false;
      },
      error: (err: unknown) => {
        this.loadingSummary = false;
        this.error = this.formatApiError(err, `Failed to load dashboard metrics from ${this.apiBase}.`);
      }
    });
  }

  /** Full lead list — used on the Leads tab (large response). */
  loadDashboardLeads(): void {
    if (this.loadingLeads) {
      return;
    }
    this.loadingLeads = true;
    this.error = '';
    this.http.get<{ leads: DashboardLead[] }>(`${this.apiBase}/api/dashboard/leads`).subscribe({
      next: (res) => {
        this.dashboardLeads = res.leads ?? [];
        this.dashboardLeadsLoaded = true;
        this.currentPage = 1;
        this.loadingLeads = false;
      },
      error: (err: unknown) => {
        this.loadingLeads = false;
        this.error = this.formatApiError(err, `Failed to load leads from ${this.apiBase}.`);
      }
    });
  }

  private refreshDashboardAfterImport(): void {
    this.error = '';
    this.loadingSummary = true;
    this.loadingLeads = true;
    this.http.get<DashboardSummaryResponse>(`${this.apiBase}/api/dashboard/summary`).subscribe({
      next: (value) => {
        this.dashboardSummary = value;
        this.currentPage = 1;
        this.loadingSummary = false;
      },
      error: (err: unknown) => {
        this.loadingSummary = false;
        this.error = this.formatApiError(err, `Failed to load dashboard metrics from ${this.apiBase}.`);
      }
    });
    this.http.get<{ leads: DashboardLead[] }>(`${this.apiBase}/api/dashboard/leads`).subscribe({
      next: (res) => {
        this.dashboardLeads = res.leads ?? [];
        this.dashboardLeadsLoaded = true;
        this.loadingLeads = false;
      },
      error: (err: unknown) => {
        this.loadingLeads = false;
        this.error = this.formatApiError(err, `Failed to load leads from ${this.apiBase}.`);
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0];
  }

  importLeads(): void {
    this.importMessage = '';
    this.importError = '';

    if (!this.selectedFile) {
      this.importError = 'Choose a CSV or XLSX file first.';
      return;
    }

    const src = this.source.trim();
    if (!src) {
      this.importError = 'Enter an import source label (how you refer to this file or vendor).';
      return;
    }

    const formData = new FormData();
    formData.append('file', this.selectedFile);
    formData.append('source', src);

    this.importing = true;
    this.http.post<LeadImportResult>(`${this.apiBase}/api/leads/import-file`, formData).subscribe({
      next: (result) => {
        this.importing = false;
        this.importMessage = `Processed ${result.processed}. Imported ${result.imported}, updated ${result.updated}, skipped ${result.skipped}.`;
        this.refreshDashboardAfterImport();
      },
      error: () => {
        this.importing = false;
        this.importError = 'Import failed. Verify API is running and file format is valid.';
      }
    });
  }

  /** Source filter values for the combobox (empty = all). */
  get leadsSourceComboboxOptions(): string[] {
    return ['', ...this.leadsSourceFilterSelectOptions];
  }

  /** Campaign filter values for the combobox (empty = all; sentinel = no campaign). */
  get leadsCampaignComboboxOptions(): string[] {
    return ['', this.leadsCampaignNoneSentinel, ...this.leadsCampaignFilterSelectOptions];
  }

  readonly leadsSourceComboboxLabelFn = (v: string): string => {
    if (v === '') {
      return 'All sources';
    }
    return this.sourceFilterOptionLabel(v);
  };

  readonly leadsCampaignComboboxLabelFn = (v: string): string => {
    const counts = this.campaignCountsBySourceFilteredLeads();
    let n: number;
    if (v === '') {
      n = this.leadsMatchingSourceOnly.length;
    } else {
      n = counts.get(v) ?? 0;
    }
    if (v === '') {
      return `All campaigns (${n})`;
    }
    if (v === this.leadsCampaignNoneSentinel) {
      return `None (${n})`;
    }
    return `${v} (${n})`;
  };

  /** Distinct source labels for the filter dropdown (canonical list plus any extra values present in data). */
  get leadsSourceFilterSelectOptions(): string[] {
    const known: string[] = [...this.knownEventSourceLabels];
    const extra = new Set<string>();
    for (const l of this.data?.leads ?? []) {
      const s = l.lastEventSource?.trim();
      if (s && !known.includes(s)) {
        extra.add(s);
      }
    }
    return [...known, ...Array.from(extra).sort((a, b) => a.localeCompare(b))];
  }

  /** Distinct non-empty campaign values from loaded leads. */
  get leadsCampaignFilterSelectOptions(): string[] {
    const set = new Set<string>();
    for (const l of this.data?.leads ?? []) {
      const c = l.lastEventCampaign?.trim();
      if (c) {
        set.add(c);
      }
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }

  /** Same source rules as table; excludes campaign filter (used for campaign dropdown counts). */
  private get leadsMatchingSourceOnly(): DashboardLead[] {
    if (!this.data) {
      return [];
    }
    let list = this.data.leads;
    const sf = this.leadsSourceFilter.trim();
    if (sf) {
      list = list.filter((l) => {
        const v = (l.lastEventSource ?? '').trim();
        if (sf === 'Unknown') {
          return !v || v === 'Unknown';
        }
        return v === sf;
      });
    }
    return list;
  }

  private campaignCountsBySourceFilteredLeads(): Map<string, number> {
    const map = new Map<string, number>();
    for (const l of this.leadsMatchingSourceOnly) {
      const c = (l.lastEventCampaign ?? '').trim();
      const key = c ? c : this.leadsCampaignNoneSentinel;
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return map;
  }

  get leadsMatchingTableFilters(): DashboardLead[] {
    let list = this.leadsMatchingSourceOnly;

    const cf = this.leadsCampaignFilter.trim();
    if (cf) {
      list = list.filter((l) => {
        const c = (l.lastEventCampaign ?? '').trim();
        if (cf === this.leadsCampaignNoneSentinel) {
          return !c;
        }
        return c === cf;
      });
    }

    if (this.leadsStageFilter) {
      list = list.filter((l) => l.stage === this.leadsStageFilter);
    }

    if (this.leadsSignupFilter === 'signed-up') {
      list = list.filter((l) => l.signupCompleted);
    } else if (this.leadsSignupFilter === 'not-signed-up') {
      list = list.filter((l) => !l.signupCompleted);
    }

    return list;
  }

  get totalLeads(): number {
    return this.leadsMatchingTableFilters.length;
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalLeads / this.pageSize));
  }

  get paginatedLeads(): DashboardLead[] {
    const list = this.leadsMatchingTableFilters;
    const start = (this.currentPage - 1) * this.pageSize;
    return list.slice(start, start + this.pageSize);
  }

  onLeadsTableFilterChange(): void {
    this.currentPage = 1;
  }

  /** Display label for source filter options (`Unknown` is shown as "Others"). */
  sourceFilterOptionLabel(value: string): string {
    return value === 'Unknown' ? 'Others' : value;
  }

  previousPage(): void {
    if (this.currentPage > 1) {
      this.currentPage--;
    }
  }

  nextPage(): void {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
    }
  }

  onPageSizeChange(): void {
    this.currentPage = 1;
  }

  private static readonly leadsExportColumns = [
    'Email',
    'Score',
    'Stage',
    'Last activity',
    'Last event',
    'Source',
    'Campaign',
    'Next date',
    'Last email',
    'Next email',
    'Next stage',
    'Signup',
    'Profile',
    'Plan'
  ] as const;

  leadsExportFilterSummary(): string {
    const parts: string[] = [];
    const sf = this.leadsSourceFilter.trim();
    if (sf) {
      parts.push(`Source: ${this.sourceFilterOptionLabel(sf)}`);
    }
    const cf = this.leadsCampaignFilter.trim();
    if (cf) {
      const label =
        cf === this.leadsCampaignNoneSentinel ? 'None' : cf;
      parts.push(`Campaign: ${label}`);
    }
    if (this.leadsStageFilter) {
      parts.push(`Stage: ${this.leadsStageFilter === 'Mql' ? 'MQL' : this.leadsStageFilter}`);
    }
    if (this.leadsSignupFilter === 'signed-up') {
      parts.push('Signup: Signed up');
    } else if (this.leadsSignupFilter === 'not-signed-up') {
      parts.push('Signup: Not signed up');
    }
    return parts.length ? parts.join(' · ') : 'No filters (all leads)';
  }

  private leadsExportFilenameSlug(): string {
    if (this.leadsStageFilter) {
      return this.leadsStageFilter.toLowerCase();
    }
    return 'all-stages';
  }

  private formatLeadExportDate(iso: string | null | undefined): string {
    if (!iso) {
      return '—';
    }
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString();
  }

  private leadRowCellsForExport(lead: DashboardLead): string[] {
    return [
      lead.email,
      String(lead.score),
      lead.stage,
      this.formatLeadExportDate(lead.lastActivityUtc),
      lead.lastEvent?.trim() ? lead.lastEvent : '—',
      lead.lastEventSource?.trim() ? lead.lastEventSource : '—',
      lead.lastEventCampaign?.trim() ? lead.lastEventCampaign : '—',
      lead.nextDateTimeUtc ? this.formatLeadExportDate(lead.nextDateTimeUtc) : '—',
      lead.lastEmailName?.trim() ? lead.lastEmailName : '—',
      lead.nextEmailName?.trim() ? lead.nextEmailName : '—',
      lead.nextStage,
      lead.signupCompleted ? 'Yes' : 'No',
      lead.profileCompletion ? 'Yes' : 'No',
      lead.profileCompletion && lead.selectedPlan?.trim() ? lead.selectedPlan : '—'
    ];
  }

  downloadLeadsFilteredPdf(): void {
    const rows = this.leadsMatchingTableFilters;
    if (!this.data || rows.length === 0) {
      this.error = 'No leads match the current filters.';
      return;
    }

    this.error = '';
    const doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });
    const generatedAt = new Date();
    doc.setFontSize(16);
    doc.text('Leads report', 40, 40);
    doc.setFontSize(10);
    doc.text(`Generated: ${generatedAt.toLocaleString()}`, 40, 58);
    const summary = this.leadsExportFilterSummary();
    doc.text(`Filters: ${summary}`, 40, 74);
    doc.text(`Rows: ${rows.length}`, 40, 90);

    const body = rows.map((lead) => this.leadRowCellsForExport(lead));
    autoTable(doc, {
      startY: 102,
      head: [Array.from(WorkspaceComponent.leadsExportColumns)],
      body,
      styles: {
        fontSize: 7,
        cellPadding: 4
      },
      headStyles: {
        fillColor: [173, 216, 230],
        textColor: [17, 24, 39],
        fontStyle: 'bold'
      },
      didParseCell: (hookData) => {
        if (hookData.section !== 'body') {
          return;
        }
        const col = hookData.column.index;
        if (col === 2 || col === 10) {
          const stage = String(hookData.cell.raw);
          if (stage === 'Cold') {
            hookData.cell.styles.textColor = [30, 64, 175];
          } else if (stage === 'Warm') {
            hookData.cell.styles.textColor = [180, 83, 9];
          } else if (stage === 'Mql') {
            hookData.cell.styles.textColor = [3, 105, 161];
          } else if (stage === 'Hot') {
            hookData.cell.styles.textColor = [185, 28, 28];
          }
        }
      }
    });

    const filename = `leads-report-${this.leadsExportFilenameSlug()}-${generatedAt.toISOString().slice(0, 10)}.pdf`;
    doc.save(filename);
  }

  downloadPdfReport(): void {
    const writePdf = (): void => {
      const snapshot = this.data;
      if (!snapshot || snapshot.leads.length === 0) {
        this.error = 'No leads available to generate report.';
        return;
      }

      this.error = '';
      const doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });
      const generatedAt = new Date();
      doc.setFontSize(16);
      doc.text('Lead Scoring Report', 40, 40);
      doc.setFontSize(10);
      doc.text(`Generated: ${generatedAt.toLocaleString()}`, 40, 58);
      doc.text(`Total Leads: ${snapshot.totalLeads}`, 40, 74);

      const body = snapshot.leads.map((lead) => [
        lead.email,
        String(lead.score),
        lead.stage,
        new Date(lead.lastActivityUtc).toLocaleString(),
        lead.lastScoredAtUtc ? new Date(lead.lastScoredAtUtc).toLocaleString() : '-'
      ]);

      autoTable(doc, {
        startY: 90,
        head: [['Email', 'Score', 'Stage', 'Last Activity', 'Last Scored']],
        body,
        styles: {
          fontSize: 9,
          cellPadding: 6
        },
        headStyles: {
          fillColor: [173, 216, 230],
          textColor: [17, 24, 39],
          fontStyle: 'bold'
        },
        didParseCell: (hookData) => {
          if (hookData.section === 'body' && hookData.column.index === 2) {
            const stage = String(hookData.cell.raw);
            if (stage === 'Cold') {
              hookData.cell.styles.textColor = [30, 64, 175];
            } else if (stage === 'Warm') {
              hookData.cell.styles.textColor = [180, 83, 9];
            } else if (stage === 'Mql') {
              hookData.cell.styles.textColor = [3, 105, 161];
            } else if (stage === 'Hot') {
              hookData.cell.styles.textColor = [185, 28, 28];
            }
          }
        }
      });

      const filename = `lead-scoring-report-${generatedAt.toISOString().slice(0, 10)}.pdf`;
      doc.save(filename);
    };

    if (this.dashboardLeadsLoaded && this.dashboardLeads.length > 0) {
      writePdf();
      return;
    }

    const totalHint = this.dashboardSummary?.totalLeads ?? 0;
    if (totalHint === 0) {
      this.error = 'No leads available to generate report.';
      return;
    }

    if (this.loadingLeads) {
      return;
    }
    this.loadingLeads = true;
    this.error = '';
    this.http.get<{ leads: DashboardLead[] }>(`${this.apiBase}/api/dashboard/leads`).subscribe({
      next: (res) => {
        this.dashboardLeads = res.leads ?? [];
        this.dashboardLeadsLoaded = true;
        this.loadingLeads = false;
        writePdf();
      },
      error: (err: unknown) => {
        this.loadingLeads = false;
        this.error = this.formatApiError(err, `Failed to load leads from ${this.apiBase}.`);
      }
    });
  }

  addEventItem(): void {
    this.companyProductForm.items.push({ eventName: '', score: '' });
  }

  removeEventItem(index: number): void {
    if (this.companyProductForm.items.length === 1) {
      return;
    }

    this.companyProductForm.items.splice(index, 1);
  }

  addEditEventItem(): void {
    this.companyProductEditForm.items.push({ eventName: '', score: '' });
  }

  removeEditEventItem(index: number): void {
    if (this.companyProductEditForm.items.length === 1) {
      return;
    }
    this.companyProductEditForm.items.splice(index, 1);
  }

  private parseStageThresholdInputs(
    warmRaw: string,
    mqlRaw: string,
    hotRaw: string
  ):
    | { ok: true; stageThresholds: StageScoreThresholds }
    | { ok: false; message: string } {
    const toInt = (raw: string | number): number | undefined => {
      const t = String(raw).trim();
      if (t === '') {
        return undefined;
      }
      const n = Number(t);
      if (!Number.isInteger(n)) {
        return undefined;
      }
      return n;
    };

    const warm = toInt(warmRaw);
    const mql = toInt(mqlRaw);
    const hot = toInt(hotRaw);
    if (warm === undefined || mql === undefined || hot === undefined) {
      return {
        ok: false,
        message: 'Enter whole-number minimum scores for Warm, MQL, and Hot thresholds.'
      };
    }
    if (warm < 1) {
      return {
        ok: false,
        message: 'Warm threshold must be at least 1; Cold applies to scores strictly below Warm.'
      };
    }
    if (mql <= warm) {
      return {
        ok: false,
        message: 'The MQL threshold must be greater than the Warm threshold.'
      };
    }
    if (hot <= mql) {
      return {
        ok: false,
        message: 'The Hot threshold must be greater than the MQL threshold.'
      };
    }
    return { ok: true, stageThresholds: { warmMin: warm, mqlMin: mql, hotMin: hot } };
  }

  stageThresholdCaption(config: CompanyProductConfig): string {
    const st = config.stageThresholds;
    const w = st?.warmMin ?? 51;
    const m = st?.mqlMin ?? 101;
    const h = st?.hotMin ?? 151;
    return `Stage thresholds · Warm from ${w} · MQL from ${m} · Hot from ${h}`;
  }

  saveCompanyConfig(): void {
    this.configError = '';
    this.configSuccess = '';

    const companyName = this.companyProductForm.companyName.trim();
    const productName = this.companyProductForm.productName.trim();
    if (!companyName || !productName) {
      this.configError = 'Company name and product name are required.';
      return;
    }

    const eventPairs: { eventName: string; score: number }[] = [];
    for (const item of this.companyProductForm.items) {
      const eventName = item.eventName.trim();
      if (!eventName) {
        continue;
      }
      const num = item.score === '' ? NaN : Number(item.score);
      if (!Number.isFinite(num) || num < 0) {
        this.configError = 'Enter a score (0 or greater) for each event that has a name.';
        return;
      }
      eventPairs.push({ eventName, score: num });
    }

    if (eventPairs.length === 0) {
      this.configError = 'Add at least one event with a name and score.';
      return;
    }

    const productEventConfig: Record<string, number> = {};
    for (const item of eventPairs) {
      productEventConfig[item.eventName] = Math.max(0, item.score);
    }

    const stageParse = this.parseStageThresholdInputs(
      this.companyProductForm.warmMinScore,
      this.companyProductForm.mqlMinScore,
      this.companyProductForm.hotMinScore
    );
    if (!stageParse.ok) {
      this.configError = stageParse.message;
      return;
    }

    const payload: UpsertCompanyProductConfigRequest = {
      companyName,
      productName,
      productEventConfig,
      stageThresholds: stageParse.stageThresholds
    };

    const existing = this.findSavedCompanyProductPair(companyName, productName);
    const updatingId = existing?.id ?? null;

    this.savingConfig = true;

    const request$ =
      updatingId !== null && updatingId.trim() !== ''
        ? this.http.put<CompanyProductConfig>(
            `${this.apiBase}/api/company-product-configs/${encodeURIComponent(updatingId)}`,
            payload
          )
        : this.http.post<CompanyProductConfig>(`${this.apiBase}/api/company-product-configs`, payload);

    request$.subscribe({
      next: () => {
        this.savingConfig = false;
        const wasUpdating = updatingId !== null && updatingId.trim() !== '';
        this.configSuccess = wasUpdating
          ? 'Company product config updated.'
          : 'Company product config saved.';
        if (!wasUpdating) {
          this.resetCompanyConfigForm(companyName);
        }
        this.reloadCompanyProductViews();
      },
      error: () => {
        this.savingConfig = false;
        this.configError = 'Failed to save config. Check API and values.';
      }
    });
  }

  saveCompanyConfigEditModal(): void {
    const id = this.companyConfigEditModalId;
    if (!id) {
      return;
    }

    this.configEditModalError = '';

    const companyName = this.companyProductEditForm.companyName.trim();
    const productName = this.companyProductEditForm.productName.trim();
    if (!companyName || !productName) {
      this.configEditModalError = 'Company name and product name are required.';
      return;
    }

    const eventPairs: { eventName: string; score: number }[] = [];
    for (const item of this.companyProductEditForm.items) {
      const eventName = item.eventName.trim();
      if (!eventName) {
        continue;
      }
      const num = item.score === '' ? NaN : Number(item.score);
      if (!Number.isFinite(num) || num < 0) {
        this.configEditModalError = 'Enter a score (0 or greater) for each event that has a name.';
        return;
      }
      eventPairs.push({ eventName, score: num });
    }

    if (eventPairs.length === 0) {
      this.configEditModalError = 'Add at least one event with a name and score.';
      return;
    }

    const productEventConfig: Record<string, number> = {};
    for (const item of eventPairs) {
      productEventConfig[item.eventName] = Math.max(0, item.score);
    }

    const stageParse = this.parseStageThresholdInputs(
      this.companyProductEditForm.warmMinScore,
      this.companyProductEditForm.mqlMinScore,
      this.companyProductEditForm.hotMinScore
    );
    if (!stageParse.ok) {
      this.configEditModalError = stageParse.message;
      return;
    }

    const payload: UpsertCompanyProductConfigRequest = {
      companyName,
      productName,
      productEventConfig,
      stageThresholds: stageParse.stageThresholds
    };

    this.savingEditModal = true;
    this.http.put<CompanyProductConfig>(`${this.apiBase}/api/company-product-configs/${id}`, payload).subscribe({
      next: () => {
        this.savingEditModal = false;
        this.configSuccess = 'Company product config updated.';
        this.configError = '';
        this.closeCompanyConfigEditModal();
        this.reloadCompanyProductViews();
      },
      error: () => {
        this.savingEditModal = false;
        this.configEditModalError = 'Failed to update config. Check API and values.';
      }
    });
  }

  openCompanyConfigEditModal(config: CompanyProductConfig): void {
    this.companyConfigEditModalId = config.id;
    this.companyProductEditForm = this.companyProductFormSnapshotFromSaved(
      config,
      config.companyName,
      config.productName
    );
    this.configEditModalError = '';
  }

  closeCompanyConfigEditModal(): void {
    this.companyConfigEditModalId = null;
    this.companyProductEditForm = {
      companyName: '',
      productName: '',
      items: [{ eventName: '', score: '' }],
      warmMinScore: WorkspaceComponent.defaultStageFormValues.warmMinScore,
      mqlMinScore: WorkspaceComponent.defaultStageFormValues.mqlMinScore,
      hotMinScore: WorkspaceComponent.defaultStageFormValues.hotMinScore
    };
    this.configEditModalError = '';
    this.savingEditModal = false;
  }

  openCompanyConfigDeleteModal(config: CompanyProductConfig): void {
    this.companyConfigDeleteTarget = config;
  }

  cancelCompanyConfigDeleteModal(): void {
    this.companyConfigDeleteTarget = null;
  }

  @HostListener('document:keydown.escape')
  onEscapeCloseCompanyConfigModals(): void {
    if (this.companyConfigEditModalId) {
      this.closeCompanyConfigEditModal();
      return;
    }
    if (this.companyConfigDeleteTarget) {
      this.cancelCompanyConfigDeleteModal();
    }
  }

  confirmCompanyConfigDelete(): void {
    const config = this.companyConfigDeleteTarget;
    if (!config) {
      return;
    }
    this.companyConfigDeleteTarget = null;

    this.configError = '';
    this.deletingConfigId = config.id;
    this.http
      .post<void>(`${this.apiBase}/api/company-product-configs/${config.id}/delete`, {})
      .pipe(finalize(() => (this.deletingConfigId = null)))
      .subscribe({
        next: () => {
          this.configSuccess = 'Configuration deleted.';
          if (this.companyConfigEditModalId === config.id) {
            this.closeCompanyConfigEditModal();
          }
          this.reloadCompanyProductViews();
        },
        error: () => {
          this.configError = 'Failed to delete configuration.';
        }
      });
  }

  onCompanyConfigFilterInput(): void {
    if (this.companyConfigFilterSearchTimer !== undefined) {
      clearTimeout(this.companyConfigFilterSearchTimer);
    }
    this.companyConfigFilterSearchTimer = setTimeout(() => {
      this.companyConfigFilterSearchTimer = undefined;
      this.loadCompanyConfigs();
    }, WorkspaceComponent.companyConfigFilterDebounceMs);
  }

  loadCompanyConfigs(): void {
    this.configLoading = true;
    this.configError = '';
    const filter = this.companyNameFilter.trim();
    const query = filter ? `?companyName=${encodeURIComponent(filter)}` : '';
    this.http.get<CompanyProductConfig[]>(`${this.apiBase}/api/company-product-configs${query}`).subscribe({
      next: (records) => {
        this.configLoading = false;
        this.companyConfigs = records;
        if (!filter) {
          this.companyProductAll = records;
        }
        this.scheduleMainCompanyProductHydrate();
      },
      error: () => {
        this.configLoading = false;
        this.configError = 'Failed to load company product configs.';
      }
    });
  }

  eventConfigEntries(config: Record<string, number>): Array<{ key: string; value: number }> {
    return Object.entries(config).map(([key, value]) => ({ key, value }));
  }

  private resetCompanyConfigForm(defaultCompanyName = ''): void {
    this.companyProductForm = {
      companyName: defaultCompanyName,
      productName: '',
      items: [{ eventName: '', score: '' }],
      warmMinScore: WorkspaceComponent.defaultStageFormValues.warmMinScore,
      mqlMinScore: WorkspaceComponent.defaultStageFormValues.mqlMinScore,
      hotMinScore: WorkspaceComponent.defaultStageFormValues.hotMinScore
    };
  }

  /** Reload company-config grid; when a name filter is active, also fetch the full list for combobox suggestions (one extra GET). */
  private reloadCompanyProductViews(): void {
    this.loadCompanyConfigs();
    if (this.companyNameFilter.trim()) {
      this.refreshCompanyProductIndex();
    }
  }

  /** Full config list for combobox suggestions (unaffected by table filter). */
  private refreshCompanyProductIndex(): void {
    this.http.get<CompanyProductConfig[]>(`${this.apiBase}/api/company-product-configs`).subscribe({
      next: (rows) => {
        this.companyProductAll = rows;
        this.scheduleMainCompanyProductHydrate();
      },
      error: () => {
        /* keep previous suggestions on failure */
      }
    });
  }

  /** Absolute origin for `/r` links: same rules as `apiBase`, with origin fallback when API is same-host relative. */
  private universalLinkApiBase(): string {
    const configured = this.apiBase.trim();
    if (configured) {
      return configured;
    }
    if (typeof window !== 'undefined') {
      return window.location.origin;
    }
    return '';
  }

  private formatApiError(err: unknown, fallback: string): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 0) {
        return `${fallback} Cannot reach the API at ${this.apiBase}. Start the backend and check CORS.`;
      }
      const body = err.error as { message?: string; detail?: string } | null;
      const msg = body?.message ?? body?.detail;
      if (msg) {
        return msg;
      }
      if (err.status === 401) {
        return 'Session expired. Please sign in again.';
      }
      if (err.status === 403) {
        return 'Access denied for your company account. Sign in again.';
      }
      return `${fallback} (HTTP ${err.status})`;
    }
    return fallback;
  }

  private resolveApiBase(): string {
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

    // In deployed environments, use same-origin and let nginx proxy /api.
    return '';
  }

  private isPrivateIpv4Host(host: string): boolean {
    const parts = host.split('.').map((x) => Number(x));
    if (parts.length !== 4 || parts.some((x) => Number.isNaN(x) || x < 0 || x > 255)) {
      return false;
    }

    const [a, b] = parts;
    return a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168);
  }

  previewManualBatch(): void {
    this.manualError = '';
    this.manualLoading = true;
    this.http
      .get<BatchPreviewResult>(`${this.apiBase}/api/batch/preview?batchType=${encodeURIComponent(this.manualBatchType)}`)
      .pipe(finalize(() => (this.manualLoading = false)))
      .subscribe({
        next: (res) => {
          const wasEmpty = !this.manualPreview;
          this.manualPreview = res;
          if (this.pendingResetTakeToFullBucket || wasEmpty) {
            this.pendingResetTakeToFullBucket = false;
            this.resetManualLeadTakeToBucketSize();
          } else {
            this.clampManualLeadTakeAfterPreview();
          }
        },
        error: () => {
          this.manualError = 'Could not load recipient counts. Verify the API or your network connection.';
        }
      });
  }

  onManualBatchTypeChange(): void {
    this.pendingResetTakeToFullBucket = true;
    this.previewManualBatch();
  }

  onManualLeadTakeChange(raw: number | string | null): void {
    const n = this.manualSelectedBucketCount;
    if (n <= 0) {
      this.manualLeadTakeCount = 1;
      return;
    }
    let v =
      typeof raw === 'number' && !Number.isNaN(raw)
        ? Math.floor(raw)
        : Number.parseInt(String(raw ?? ''), 10);
    if (Number.isNaN(v)) {
      v = 1;
    }
    this.manualLeadTakeCount = Math.min(Math.max(1, v), n);
  }

  /** Maps API numeric enum or string batch type to the same labels as the manual batch template dropdown. */
  manualBatchTypeLabel(batchType: unknown): string {
    let code: number | null = null;
    if (typeof batchType === 'number' && Number.isFinite(batchType)) {
      code = batchType;
    } else if (typeof batchType === 'string') {
      const nameToCode: Record<string, number> = {
        Day1: 1,
        Day2: 2,
        Day3: 3,
        Day4: 4,
        Warm: 5,
        WarmFollowUp: 6,
        Mql: 7,
        MqlFollowUp: 8,
        Hot: 9,
        HotFollowUp: 10
      };
      if (batchType in nameToCode) {
        code = nameToCode[batchType];
      } else {
        const parsed = Number.parseInt(batchType, 10);
        if (!Number.isNaN(parsed)) {
          code = parsed;
        }
      }
    } else {
      const parsed = Number.parseInt(String(batchType ?? ''), 10);
      if (!Number.isNaN(parsed)) {
        code = parsed;
      }
    }
    const labels: Record<number, string> = {
      1: 'COLD',
      2: 'COLD FOLLOWUP',
      3: 'Day 3 (automated mix)',
      4: 'Day 4 (automated)',
      5: 'WARM',
      6: 'WARM FOLLOWUP',
      7: 'MQL',
      8: 'MQL FOLLOWUP',
      9: 'HOT',
      10: 'HOT FOLLOWUP'
    };
    if (code !== null && labels[code]) {
      return labels[code];
    }
    return String(batchType ?? '');
  }

  manualScopeLabel(scope: unknown): string {
    const key = String(scope ?? '');
    const map: Record<string, string> = {
      TotalEligible: 'Total eligible',
      TotalLeads: 'Total leads',
      Stage0: 'Stage 0',
      Stage1: 'Stage 1',
      Stage2: 'Stage 2',
      Stage3: 'Stage 3',
      Stage4: 'Stage 4',
      NewLeads: 'New leads',
      Last2DaysInactive: 'Last 2 days inactive',
      Last4DaysSinceEmail: 'Last 4 days since email',
      DidNotOpenEmail: 'Did not open email'
    };
    return map[key] ?? key;
  }

  private resetManualLeadTakeToBucketSize(): void {
    const n = this.manualSelectedBucketCount;
    this.manualLeadTakeCount = n > 0 ? n : 1;
  }

  private clampManualLeadTakeAfterPreview(): void {
    const n = this.manualSelectedBucketCount;
    if (n <= 0) {
      this.manualLeadTakeCount = 1;
      return;
    }
    this.manualLeadTakeCount = Math.min(Math.max(1, Math.floor(this.manualLeadTakeCount)), n);
  }

  runManualBatch(): void {
    this.manualError = '';
    this.manualLoading = true;
    this.manualRunResult = undefined;
    this.http
      .post<BatchManualRunStart>(`${this.apiBase}/api/batch/run-manual/start?batchType=${encodeURIComponent(this.manualBatchType)}`, (() => {
        const n = this.manualSelectedBucketCount;
        const take = Math.min(Math.max(1, Math.floor(this.manualLeadTakeCount)), n);
        const payload: { scope: ManualScope; maxLeads?: number } = { scope: this.manualScope };
        if (take < n) {
          payload.maxLeads = take;
        }
        return payload;
      })())
      .pipe(finalize(() => (this.manualLoading = false)))
      .subscribe({
        next: (res) => {
          this.manualRunJobId = res.jobId;
          this.manualRunStatus = {
            jobId: res.jobId,
            batchType: res.batchType,
            scope: res.scope,
            isRunning: true,
            totalLeads: 0,
            processedCount: 0,
            successCount: 0,
            failureCount: 0
          };
          this.startManualRunPolling(res.jobId);
        },
        error: () => {
          this.manualError = 'Could not start this send. Verify the API or try again.';
        }
      });
  }

  private startManualRunPolling(jobId: string): void {
    this.stopManualRunPolling();

    const poll = (): void => {
      this.http
        .get<BatchManualRunStatus>(`${this.apiBase}/api/batch/run-manual/status/${encodeURIComponent(jobId)}`)
        .subscribe({
          next: (status) => {
            this.manualRunStatus = status;
            if (!status.isRunning) {
              this.stopManualRunPolling();
              if (status.result) {
                this.manualRunResult = status.result;
                this.manualRunHistory.unshift({
                  runAtUtc: new Date().toISOString(),
                  scope: status.scope,
                  result: status.result
                });
              }
              // Bucket counts (e.g. Last 2 days inactive) and stage totals reflect DB changes only after preview refresh.
              this.previewManualBatch();
              this.loadBatchLogHistory();
            }
          },
          error: () => {
            this.stopManualRunPolling();
            this.manualError = 'Connection lost while fetching send progress.';
          }
        });
    };

    poll();
    this.manualRunPollTimer = setInterval(poll, 1200);
  }

  private stopManualRunPolling(): void {
    if (this.manualRunPollTimer !== undefined) {
      clearInterval(this.manualRunPollTimer);
      this.manualRunPollTimer = undefined;
    }
  }

  loadBatchLogHistory(): void {
    this.batchLogHistoryLoading = true;
    this.batchLogHistoryError = '';
    this.http
      .get<BatchLogHistoryRow[]>(`${this.apiBase}/api/batch/history?take=200`)
      .pipe(finalize(() => (this.batchLogHistoryLoading = false)))
      .subscribe({
        next: (rows) => {
          this.batchLogHistoryRows = rows ?? [];
          this.batchLogHistoryPage = 1;
          this.clampBatchLogHistoryPage();
        },
        error: () => {
          this.batchLogHistoryError = 'Could not load batch history.';
        }
      });
  }

  get batchLogHistoryTotalPages(): number {
    const n = this.batchLogHistoryRows.length;
    if (n === 0) {
      return 0;
    }
    return Math.ceil(n / this.batchLogHistoryPageSize);
  }

  get batchLogHistoryPagedRows(): BatchLogHistoryRow[] {
    const start = (this.batchLogHistoryPage - 1) * this.batchLogHistoryPageSize;
    return this.batchLogHistoryRows.slice(start, start + this.batchLogHistoryPageSize);
  }

  get batchLogHistoryRangeStart(): number {
    if (this.batchLogHistoryRows.length === 0) {
      return 0;
    }
    return (this.batchLogHistoryPage - 1) * this.batchLogHistoryPageSize + 1;
  }

  get batchLogHistoryRangeEnd(): number {
    return Math.min(this.batchLogHistoryPage * this.batchLogHistoryPageSize, this.batchLogHistoryRows.length);
  }

  batchLogHistoryPrevPage(): void {
    if (this.batchLogHistoryPage > 1) {
      this.batchLogHistoryPage--;
    }
  }

  batchLogHistoryNextPage(): void {
    if (this.batchLogHistoryPage < this.batchLogHistoryTotalPages) {
      this.batchLogHistoryPage++;
    }
  }

  private clampBatchLogHistoryPage(): void {
    const tp = this.batchLogHistoryTotalPages;
    if (tp === 0) {
      this.batchLogHistoryPage = 1;
      return;
    }
    if (this.batchLogHistoryPage > tp) {
      this.batchLogHistoryPage = tp;
    }
    if (this.batchLogHistoryPage < 1) {
      this.batchLogHistoryPage = 1;
    }
  }

  batchLogSuccessRatePct(row: BatchLogHistoryRow): number | null {
    const t = row.totalLeadsProcessed;
    if (t <= 0) {
      return null;
    }
    return Math.round((row.successCount / t) * 1000) / 10;
  }

  batchLogSuccessRateDisplay(row: BatchLogHistoryRow): string {
    const p = this.batchLogSuccessRatePct(row);
    return p === null ? '—' : `${p}%`;
  }
}

type StageName = 'Cold' | 'Warm' | 'Mql' | 'Hot';
type EventName = 'Open' | 'EmailClick' | 'WebsiteActivity';

interface DashboardLead {
  id: string;
  email: string;
  score: number;
  stage: StageName;
  lastActivityUtc: string;
  lastScoredAtUtc?: string | null;
  lastEvent?: string | null;
  lastEventSource?: string | null;
  lastEventCampaign?: string | null;
  lastEmailName?: string | null;
  nextEmailName?: string | null;
  nextDateTimeUtc?: string | null;
  nextStage: StageName;
  signupCompleted: boolean;
  profileCompletion: boolean;
  selectedPlan?: string | null;
  planRenewalDate?: string | null;
}

interface DashboardSummaryResponse {
  companyName?: string;
  totalLeads: number;
  signedUpCount: number;
  stageCounts: Record<StageName, number>;
  eventsByType: Record<EventName, number>;
  firstSourceCounts: Record<string, number>;
}

interface DashboardResponse {
  totalLeads: number;
  signedUpCount: number;
  stageCounts: Record<StageName, number>;
  eventsByType: Record<EventName, number>;
  firstSourceCounts: Record<string, number>;
  leads: DashboardLead[];
}

interface LeadImportResult {
  processed: number;
  imported: number;
  updated: number;
  skipped: number;
  errors: string[];
}

interface StageScoreThresholds {
  warmMin: number;
  mqlMin: number;
  hotMin: number;
}

interface CompanyProductForm {
  companyName: string;
  productName: string;
  items: CompanyProductEventItem[];
  warmMinScore: string;
  mqlMinScore: string;
  hotMinScore: string;
}

interface CompanyProductEventItem {
  eventName: string;
  score: number | '';
}

interface UpsertCompanyProductConfigRequest {
  companyName: string;
  productName: string;
  productEventConfig: Record<string, number>;
  stageThresholds?: StageScoreThresholds;
}

interface CompanyProductConfig {
  id: string;
  companyName: string;
  productName: string;
  productId: number;
  productEventConfig: Record<string, number>;
  stageThresholds?: StageScoreThresholds;
  createdAtUtc: string;
}

type LeftTab = 'dashboard' | 'leads' | 'company-config' | 'tracking-links' | 'website-script' | 'manual-batch';
type ManualBatchType =
  | 'Day1'
  | 'Day2'
  | 'Warm'
  | 'WarmFollowUp'
  | 'Mql'
  | 'MqlFollowUp'
  | 'Hot'
  | 'HotFollowUp';
type ManualScope =
  | 'TotalEligible'
  | 'TotalLeads'
  | 'Stage0'
  | 'Stage1'
  | 'Stage2'
  | 'Stage3'
  | 'Stage4'
  | 'NewLeads'
  | 'Last2DaysInactive'
  | 'Last4DaysSinceEmail'
  | 'DidNotOpenEmail';

interface BatchPreviewResult {
  /** Signed-in tenant / workspace company label (camelCase JSON). Optional for older backends. */
  companyName?: string;
  /** API may serialize `CampaignBatchType` as a number (1–10). */
  batchType: ManualBatchType | number;
  totalLeadsCount: number;
  stage0Count: number;
  stage1Count: number;
  stage2Count: number;
  stage3Count: number;
  stage4Count: number;
  newLeadsCount: number;
  last2DaysInactiveCount: number;
  last4DaysSinceLastEmailCount: number;
  didNotOpenEmailCount: number;
  totalEligibleCount: number;
}

interface BatchFailureInfo {
  leadId: string;
  email: string;
  reason: string;
}

interface BatchManualRunResult {
  batchType: ManualBatchType | number;
  totalLeads: number;
  successCount: number;
  failureCount: number;
  failures: BatchFailureInfo[];
}

interface BatchManualRunStart {
  jobId: string;
  batchType: ManualBatchType | number;
  scope: ManualScope;
}

interface BatchManualRunStatus {
  jobId: string;
  batchType: ManualBatchType | number;
  scope: ManualScope;
  isRunning: boolean;
  totalLeads: number;
  processedCount: number;
  successCount: number;
  failureCount: number;
  result?: BatchManualRunResult;
}

interface ManualRunHistoryItem {
  runAtUtc: string;
  scope: ManualScope;
  result: BatchManualRunResult;
}

/** Matches API `BatchLogHistoryDto` (camelCase JSON). */
interface BatchLogHistoryRow {
  batchId: number;
  runDateUtc: string;
  batchType: number;
  totalLeadsProcessed: number;
  successCount: number;
  failureCount: number;
}
