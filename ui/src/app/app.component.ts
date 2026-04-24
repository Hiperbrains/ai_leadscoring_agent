import { Component, OnInit, inject } from '@angular/core';
import { DecimalPipe, DatePipe, NgClass, NgFor, NgIf } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

@Component({
  selector: 'app-root',
  imports: [DecimalPipe, DatePipe, NgIf, NgFor, NgClass, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {
  private readonly http = inject(HttpClient);
  apiBase = 'http://localhost:5221';
  loading = false;
  error = '';
  data?: DashboardResponse;
  selectedFile?: File;
  source = 'hubspot';
  importMessage = '';
  importError = '';
  importing = false;
  pageSize = 10;
  currentPage = 1;

  ngOnInit(): void {
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading = true;
    this.error = '';
    this.http.get<DashboardResponse>(`${this.apiBase}/api/dashboard`).subscribe({
      next: (value) => {
        this.data = value;
        this.currentPage = 1;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.error = `Failed to load dashboard from ${this.apiBase}. Check API URL and CORS.`;
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

    const formData = new FormData();
    formData.append('file', this.selectedFile);
    formData.append('source', this.source.trim() || 'external');

    this.importing = true;
    this.http.post<LeadImportResult>(`${this.apiBase}/api/leads/import-file`, formData).subscribe({
      next: (result) => {
        this.importing = false;
        this.importMessage = `Processed ${result.processed}. Imported ${result.imported}, updated ${result.updated}, skipped ${result.skipped}.`;
        this.loadDashboard();
      },
      error: () => {
        this.importing = false;
        this.importError = 'Import failed. Verify API is running and file format is valid.';
      }
    });
  }

  get totalLeads(): number {
    return this.data?.leads.length ?? 0;
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalLeads / this.pageSize));
  }

  get paginatedLeads(): DashboardLead[] {
    if (!this.data) {
      return [];
    }

    const start = (this.currentPage - 1) * this.pageSize;
    return this.data.leads.slice(start, start + this.pageSize);
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

  getStageClass(stage: StageName): string {
    switch (stage) {
      case 'Cold':
        return 'stage-cold';
      case 'Warm':
        return 'stage-warm';
      case 'Mql':
        return 'stage-mql';
      case 'Hot':
        return 'stage-hot';
      default:
        return '';
    }
  }

  downloadPdfReport(): void {
    if (!this.data || this.data.leads.length === 0) {
      this.error = 'No leads available to generate report.';
      return;
    }

    const doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });
    const generatedAt = new Date();
    doc.setFontSize(16);
    doc.text('Lead Scoring Report', 40, 40);
    doc.setFontSize(10);
    doc.text(`Generated: ${generatedAt.toLocaleString()}`, 40, 58);
    doc.text(`Total Leads: ${this.data.totalLeads}`, 40, 74);

    const body = this.data.leads.map((lead) => [
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
}

interface DashboardResponse {
  totalLeads: number;
  stageCounts: Record<StageName, number>;
  eventsByType: Record<EventName, number>;
  leads: DashboardLead[];
}

interface LeadImportResult {
  processed: number;
  imported: number;
  updated: number;
  skipped: number;
  errors: string[];
}
