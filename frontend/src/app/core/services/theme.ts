import { Injectable, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';

@Injectable({
  providedIn: 'root',
})
export class Theme {
  private document = inject(DOCUMENT);

  private _isDark = signal(false);
  isDark = this._isDark.asReadonly();

  constructor() {
    this.setDark(true);
  }

  toggle() {
    this.setDark(!this._isDark());
  }

  setDark(value: boolean) {
    this._isDark.set(value);

    const theme = value ? 'dark' : 'light';

    // SAFE: Angular-provided document
    this.document.body.setAttribute('data-theme', theme);
  }
}
