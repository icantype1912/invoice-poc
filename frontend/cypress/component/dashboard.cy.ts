import { mount } from 'cypress/angular'
import { Dashboard } from '../../src/app/features/dashboard/dashboard'
import { provideHttpClient } from '@angular/common/http'
import { AnalyticsService } from '../../src/app/core/services/analytics.service'
import { Auth } from '../../src/app/core/services/auth'
import { ChatbotService } from '../../src/app/core/services/chatbot.service'
import { of } from 'rxjs'

describe('Dashboard Component', () => {

  const mockAnalyticsService = {
    getCategorySales: () => of([
      {
        category: 'Electronics',
        totalRevenue: 10000,
        invoiceCount: 10,
        totalQuantity: 50,
        productCount: 5,
        averageOrderValue: 1000
      }
    ]),

    getTrendingProducts: () => of([
      {
        productId: '1',
        productName: 'Laptop',
        category: 'Electronics',
        rank: 1,
        invoiceCount: 10,
        totalRevenue: 10000,
        totalQuantity: 10,
        growthRate: 0.2
      }
    ]),

    getProductSales: () => of([
      {
        productId: '1',
        productName: 'Laptop',
        category: 'Electronics',
        invoiceCount: 10,
        totalRevenue: 10000,
        totalQuantity: 10,
        averageUnitRate: 1000
      }
    ]),

    getRevenueTrend: () => of([
      {
        period: new Date(),
        revenue: 10000,
        invoiceCount: 5
      }
    ])
  }

  const mockAuth = {
    isAdmin: true
  }

  const mockChatbot = {
    isOpen: () => false,
    close: Cypress.sinon.stub()
  }

  beforeEach(() => {

    cy.intercept('GET', '**/admin/users', {
      statusCode: 200,
      body: [
        {
          id: '1',
          email: 'vendor@test.com',
          companyName: 'Test Vendor',
          role: 1,
          status: 1
        }
      ]
    })

    cy.intercept('GET', '**/invoices*', {
      statusCode: 200,
      body: {
        total: 5,
        invoices: [
          {
            id: '1',
            invoiceNumber: 'INV-001',
            createdAt: new Date().toISOString(),
            totalAmount: 500,
            currency: 'USD',
            lineItems: []
          }
        ]
      }
    })

    mount(Dashboard, {
      providers: [
        provideHttpClient(),
        { provide: AnalyticsService, useValue: mockAnalyticsService },
        { provide: Auth, useValue: mockAuth },
        { provide: ChatbotService, useValue: mockChatbot }
      ]
    })

  })

  it('renders dashboard title', () => {
    cy.contains('Analytics Dashboard')
  })

  it('renders KPI cards', () => {
    cy.contains('Total Revenue')
    cy.contains('Total Invoices')
    cy.contains('Avg. Order Value')
    cy.contains('Total Products')
  })

  it('renders vendor filter when admin', () => {
    cy.get('.vendor-select').should('exist')
  })

  it('changes range filter', () => {
    cy.contains('30D').click()
    cy.contains('90D').click()
    cy.contains('12M').click()
  })

  it('renders recent invoices', () => {
    cy.contains('Recent Invoices')
    cy.contains('INV-001')
  })

  it('renders deep dive tabs', () => {
    cy.contains('Trending Products')
    cy.contains('Top by Revenue')
    cy.contains('Categories')
  })

  it('switches deep dive tabs', () => {
    cy.contains('Top by Revenue').click()
    cy.contains('Categories').click()
  })

})