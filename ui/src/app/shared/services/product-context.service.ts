import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, tap } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { apiUrl } from '../helpers/api-base.helper';

export interface ProductOption {
  /** GUID primary key of the underlying CompanyProductConfig row. */
  id: string;
  /** Integer product id used by all backend queries and the X-Product-Id header. */
  productId: number;
  productName: string;
  productUrl?: string | null;
}

interface CompanyProductConfigDto {
  id: string;
  companyName: string;
  productName: string;
  productUrl?: string | null;
  productId: number;
}

const SELECTED_PRODUCT_KEY = 'leadScoring.selectedProductId';

@Injectable({ providedIn: 'root' })
export class ProductContextService {
  private readonly http = inject(HttpClient);

  readonly availableProducts = signal<ProductOption[]>([]);
  readonly selectedProductId = signal<number | null>(this.readPersistedId());
  readonly loading = signal(false);
  readonly loaded = signal(false);

  readonly selectedProduct = computed<ProductOption | null>(() => {
    const id = this.selectedProductId();
    if (id == null) {
      return null;
    }
    return this.availableProducts().find((p) => p.productId === id) ?? null;
  });

  readonly hasProducts = computed(() => this.availableProducts().length > 0);

  /**
   * Returns the current product id (synchronously) for the HTTP interceptor.
   * Falls back to localStorage to avoid first-paint races before products load.
   */
  currentProductId(): number | null {
    return this.selectedProductId() ?? this.readPersistedId();
  }

  loadProducts(): Observable<ProductOption[]> {
    this.loading.set(true);
    return this.http.get<CompanyProductConfigDto[]>(apiUrl('/api/company-product-configs')).pipe(
      map((rows) =>
        (rows ?? [])
          .map<ProductOption>((r) => ({
            id: r.id,
            productId: r.productId,
            productName: r.productName,
            productUrl: r.productUrl
          }))
          .sort((a, b) => a.productId - b.productId)
      ),
      tap((products) => {
        this.availableProducts.set(products);
        this.loading.set(false);
        this.loaded.set(true);
        this.reconcileSelection(products);
      }),
      catchError((err) => {
        this.loading.set(false);
        this.loaded.set(true);
        return of<ProductOption[]>([]).pipe(tap((empty) => this.availableProducts.set(empty)));
      })
    );
  }

  selectProduct(productId: number | null): void {
    if (productId === null) {
      this.selectedProductId.set(null);
      this.clearPersisted();
      return;
    }
    const exists = this.availableProducts().some((p) => p.productId === productId);
    if (!exists) {
      return;
    }
    this.selectedProductId.set(productId);
    this.persistId(productId);
  }

  /** Called by AuthService on logout to clear any cached product state. */
  clear(): void {
    this.availableProducts.set([]);
    this.selectedProductId.set(null);
    this.loaded.set(false);
    this.clearPersisted();
  }

  private reconcileSelection(products: ProductOption[]): void {
    if (products.length === 0) {
      this.selectedProductId.set(null);
      this.clearPersisted();
      return;
    }

    const persisted = this.readPersistedId();
    const current = this.selectedProductId();
    const candidate =
      (current != null && products.some((p) => p.productId === current) ? current : null) ??
      (persisted != null && products.some((p) => p.productId === persisted) ? persisted : null) ??
      products[0].productId;

    this.selectedProductId.set(candidate);
    this.persistId(candidate);
  }

  private readPersistedId(): number | null {
    if (typeof localStorage === 'undefined') {
      return null;
    }
    const raw = localStorage.getItem(SELECTED_PRODUCT_KEY);
    if (!raw) {
      return null;
    }
    const parsed = Number.parseInt(raw, 10);
    return Number.isFinite(parsed) ? parsed : null;
  }

  private persistId(id: number): void {
    if (typeof localStorage === 'undefined') {
      return;
    }
    localStorage.setItem(SELECTED_PRODUCT_KEY, String(id));
  }

  private clearPersisted(): void {
    if (typeof localStorage === 'undefined') {
      return;
    }
    localStorage.removeItem(SELECTED_PRODUCT_KEY);
  }
}
