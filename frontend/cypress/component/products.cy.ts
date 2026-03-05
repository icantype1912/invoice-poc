import { Products } from '../../src/app/features/products/products'
import { provideHttpClient } from '@angular/common/http'
import { ApiService } from '../../src/app/core/services/api.service'
import { Auth } from '../../src/app/core/services/auth'
import { of } from 'rxjs'

describe('Products Component', () => {

  const mockProductsResponse = {
    products: [
      {
        productId: 'p1',
        productName: 'Office Chair',
        category: 'Furniture',
        defaultUnitRate: 120,
        totalQuantitySold: 20,
        totalRevenue: 2400,
        invoiceCount: 4,
        lastSoldDate: '2025-01-01T00:00:00Z'
      }
    ],
    total: 1,
    totalPages: 1
  }

  const mockCategories = [
    { name: 'Furniture' },
    { name: 'Electronics' }
  ]

  const apiStub = {
    getProducts: () => of(mockProductsResponse),
    getProductCategories: () => of(mockCategories)
  }

  const authStub = {
    isAdmin: () => false
  }

  it('renders products page', () => {

    cy.mount(Products, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Products')
    cy.contains('All processed products and their sales')

  })


  it('loads and displays products', () => {

    cy.mount(Products, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Office Chair')
    cy.contains('Furniture')

  })


  it('shows empty state when no products', () => {

    const emptyApiStub = {
      getProducts: () => of({
        products: [],
        total: 0,
        totalPages: 1
      }),
      getProductCategories: () => of([])
    }

    cy.mount(Products, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: emptyApiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('No products found')

  })


  it('pagination next page works', () => {

    const paginatedApi = {
      getProducts: () => of({
        products: mockProductsResponse.products,
        total: 100,
        totalPages: 5
      }),
      getProductCategories: () => of(mockCategories)
    }

    cy.mount(Products, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: paginatedApi },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Next').click()

    cy.contains('Page 2')

  })


  it('pagination prev page works', () => {

    const paginatedApi = {
      getProducts: () => of({
        products: mockProductsResponse.products,
        total: 100,
        totalPages: 5
      }),
      getProductCategories: () => of(mockCategories)
    }

    cy.mount(Products, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: paginatedApi },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Next').click()
    cy.contains('Prev').click()

    cy.contains('Page 1')

  })


  it('shows vendor selector for admin', () => {

    const adminAuth = {
      isAdmin: () => true
    }

    cy.intercept('GET', '**/admin/users', {
      statusCode: 200,
      body: [
        {
          id: '1',
          email: 'vendor@test.com',
          companyName: 'Vendor Co',
          role: 1
        }
      ]
    }).as('vendors')

    cy.mount(Products, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: adminAuth }
      ]
    })

    cy.wait('@vendors')

    cy.contains('Vendor')

  })


  it('changes vendor filter', () => {

    const adminAuth = {
      isAdmin: () => true
    }

    cy.intercept('GET', '**/admin/users', {
      statusCode: 200,
      body: [
        {
          id: '1',
          email: 'vendor@test.com',
          companyName: 'Vendor Co',
          role: 1
        }
      ]
    }).as('vendors')

    cy.mount(Products, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: adminAuth }
      ]
    })

    cy.wait('@vendors')

    cy.get('.vendor-select').select('Vendor Co')

  })

})