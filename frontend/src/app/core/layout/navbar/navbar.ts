import { Component, computed, signal, HostListener, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Theme } from '../../services/theme';
import { Auth } from '../../services/auth';
import { ChatbotService } from '../../services/chatbot.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './navbar.html',
  styleUrls: ['./navbar.css'],
})
export class Navbar {

  logoSrc = computed(() =>
    this.theme.isDark()
      ? '/logo.svg'
      : '/logolight.svg'
  );

  isDark = computed(() => this.theme.isDark());

  dropdownOpen = signal(false);

  constructor(
    public theme: Theme,
    public auth: Auth,
    private router: Router
  ) { }

  public chatbotService = inject(ChatbotService);

  get showChatToggle(): boolean {
    return this.isLoggedIn && this.router.url.startsWith('/dashboard');
  }

  get isLoggedIn(): boolean {
    return this.auth.isLoggedIn;
  }

  get isAdmin(): boolean {
    return this.auth.isAdmin;
  }

  get isVendor(): boolean {
    return this.auth.isUser;
  }

  getUserInitials(): string {
    const email = this.auth.getEmail();
    if (!email) return '?';
    const parts = email.split('@')[0].split(/[._-]/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return email.slice(0, 2).toUpperCase();
  }

  getRoleLabel(): string {
    const role = this.auth.getRole();
    return role === 'Admin' ? 'Admin' : 'Vendor';
  }

  toggleTheme() {
    this.theme.toggle();
  }

  toggleDropdown() {
    this.dropdownOpen.update(v => !v);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    const target = event.target as HTMLElement;
    if (!target.closest('.user-menu')) {
      this.dropdownOpen.set(false);
    }
  }

  get isLandingPage(): boolean {
    return this.router.url === '/' || this.router.url === '';
  }

  logout() {
    this.dropdownOpen.set(false);
    this.auth.clearToken();
    this.router.navigate(['/login']);
  }
}

