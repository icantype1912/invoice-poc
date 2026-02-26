import { Component, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Auth } from '../../core/services/auth';
import { environment } from '../../../environments/environment';

type User = {
  id: string;
  email: string;
  companyName?: string;
  role: number;
  status: number;
};

@Component({
  selector: 'app-admin',
  standalone: true,
  templateUrl: './admin.html',
  styleUrl: './admin.css',
})
export class Admin {

  constructor(private http: HttpClient, public auth: Auth) { }

  private api = environment.apiUrl + '/admin';


  pendingUsers = signal<User[]>([]);
  users = signal<User[]>([]);

  ngOnInit() {
    this.refresh();
  }

  refresh() {
    this.loadPending();
    this.loadUsers();
  }

  isCurrentUser(id: string) {
    return this.auth.getUserId() === id;
  }


  loadPending() {
    this.http.get<User[]>(`${this.api}/users/pending`)
      .subscribe(res => this.pendingUsers.set(res));
  }

  loadUsers() {
    this.http.get<User[]>(`${this.api}/users`)
      .subscribe(res => this.users.set(res));
  }

  approve(id: string) {
    this.http.post(`${this.api}/users/${id}/approve`, {})
      .subscribe(() => this.refresh());
  }

  reject(id: string) {
    this.http.post(`${this.api}/users/${id}/reject`, { reason: 'Rejected by admin' })
      .subscribe(() => this.refresh());
  }

  promote(id: string) {
    this.http.post(`${this.api}/users/${id}/promote`, {})
      .subscribe(() => this.refresh());
  }

  delete(id: string) {
    this.http.delete(`${this.api}/users/${id}`)
      .subscribe(() => this.refresh());
  }

  unlock(id: string) {
    this.http.post(`${this.api}/users/${id}/unlock`, {})
      .subscribe(() => this.refresh());
  }

  // ---------------- LABEL HELPERS ----------------

  roleLabel(role: number) {
    if (role === 0) return 'Admin';
    if (role === 1) return 'Vendor';
    return 'Unknown';
  }

  statusLabel(status: number) {
    if (status === 0) return 'Pending';
    if (status === 1) return 'Approved';
    if (status === 2) return 'Rejected';
    if (status === 3) return 'Locked';
    return 'Unknown';
  }
}

