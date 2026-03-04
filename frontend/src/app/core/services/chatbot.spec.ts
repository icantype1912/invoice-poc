import { describe, it, expect, beforeEach } from 'vitest';
import { ChatbotService } from './chatbot.service';
 
describe('ChatbotService', () => {
  let service: ChatbotService;
 
  beforeEach(() => {
    service = new ChatbotService();
  });
 
  it('should be closed by default', () => {
    expect(service.isOpen()).toBe(false);
  });
 
  it('toggle should open when closed', () => {
    service.toggle();
    expect(service.isOpen()).toBe(true);
  });
 
  it('toggle should close when open', () => {
    service.toggle();
    service.toggle();
    expect(service.isOpen()).toBe(false);
  });
 
  it('open should set isOpen to true', () => {
    service.open();
    expect(service.isOpen()).toBe(true);
  });
 
  it('open should be idempotent', () => {
    service.open();
    service.open();
    expect(service.isOpen()).toBe(true);
  });
 
  it('close should set isOpen to false', () => {
    service.open();
    service.close();
    expect(service.isOpen()).toBe(false);
  });
 
  it('close should be idempotent', () => {
    service.close();
    service.close();
    expect(service.isOpen()).toBe(false);
  });
});
 