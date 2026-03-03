import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ChatbotService {
    /** Whether the chatbot panel is currently visible */
    isOpen = signal(false);

    toggle(): void {
        this.isOpen.update(v => !v);
    }

    open(): void {
        this.isOpen.set(true);
    }

    close(): void {
        this.isOpen.set(false);
    }
}
