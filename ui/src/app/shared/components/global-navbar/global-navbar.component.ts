import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ProductContextService } from '../../services/product-context.service';

@Component({
  selector: 'app-global-navbar',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './global-navbar.component.html',
  styleUrls: ['./global-navbar.component.scss']
})
export class GlobalNavbarComponent implements OnInit {
  private readonly auth = inject(AuthService);
  protected readonly productContext = inject(ProductContextService);

  ngOnInit(): void {
    if (this.auth.isLoggedIn() && !this.productContext.loaded()) {
      this.productContext.loadProducts().subscribe();
    }
  }

  onProductChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const raw = select.value;
    if (!raw) {
      this.productContext.selectProduct(null);
      return;
    }
    const parsed = Number.parseInt(raw, 10);
    if (Number.isFinite(parsed)) {
      this.productContext.selectProduct(parsed);
    }
  }
}
