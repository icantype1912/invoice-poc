import { Search } from '../../src/app/features/search/search'
import { provideHttpClient } from '@angular/common/http'
import { FormsModule } from '@angular/forms'
import { CommonModule } from '@angular/common'

describe('Search Component', () => {

  const mockResult = {
    naturalLanguageQuery: 'Show invoices',
    generatedSql: 'SELECT * FROM invoices',
    rowCount: 1,
    rows: [
      {
        id: 1,
        vendor: 'ACME Corp',
        amount: 1200
      }
    ]
  }

  it('renders search UI', () => {

    cy.mount(Search, {
      imports: [CommonModule, FormsModule],
      providers: [provideHttpClient()]
    })

    cy.contains('Ask your')
    cy.contains('Run')

  })


  it('updates query when typing', () => {

    cy.mount(Search, {
      imports: [CommonModule, FormsModule],
      providers: [provideHttpClient()]
    })

    cy.get('.search-input')
      .type('Show invoices')

    cy.get('.search-input')
      .should('have.value', 'Show invoices')

  })


  it('runs search and shows results', () => {

    cy.intercept('POST', '**/search', {
      statusCode: 200,
      body: mockResult
    }).as('search')

    cy.mount(Search, {
      imports: [CommonModule, FormsModule],
      providers: [provideHttpClient()]
    })

    cy.get('.search-input').type('Show invoices')

    cy.contains('Run').click()

    cy.wait('@search')

    cy.contains('1 row')
    cy.contains('ACME Corp')

  })


  it('shows loading state while searching', () => {

    cy.intercept('POST', '**/search', {
      delay: 1000,
      statusCode: 200,
      body: mockResult
    }).as('search')

    cy.mount(Search, {
      imports: [CommonModule, FormsModule],
      providers: [provideHttpClient()]
    })

    cy.get('.search-input').type('Show invoices')

    cy.contains('Run').click()

    cy.contains('Generating query')

  })


  it('shows error message from API', () => {

    cy.intercept('POST', '**/search', {
      statusCode: 200,
      body: {
        error: 'Invalid query'
      }
    }).as('search')

    cy.mount(Search, {
      imports: [CommonModule, FormsModule],
      providers: [provideHttpClient()]
    })

    cy.get('.search-input').type('bad query')

    cy.contains('Run').click()

    cy.wait('@search')

    cy.contains('Invalid query')

  })


  it('uses example query when clicked', () => {

    cy.mount(Search, {
      imports: [CommonModule, FormsModule],
      providers: [provideHttpClient()]
    })

    cy.contains('Show me all invoices from the last 30 days')
      .click()

    cy.get('.search-input')
      .should('have.value', 'Show me all invoices from the last 30 days')

  })


  it('toggles SQL view', () => {

    cy.intercept('POST', '**/search', {
      statusCode: 200,
      body: mockResult
    }).as('search')

    cy.mount(Search, {
      imports: [CommonModule, FormsModule],
      providers: [provideHttpClient()]
    })

    cy.get('.search-input').type('Show invoices')

    cy.contains('Run').click()

    cy.wait('@search')

    cy.contains('View SQL').click()

    cy.contains('SELECT * FROM invoices')

  })


  it('shows character limit warning', () => {

    cy.mount(Search, {
      imports: [CommonModule, FormsModule],
      providers: [provideHttpClient()]
    })

    const longQuery = 'a'.repeat(501)

    cy.get('.search-input')
      .type(longQuery)

    cy.contains('Query exceeds')

  })

})