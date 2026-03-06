describe('Invoices Page', () => {

  beforeEach(() => {

    cy.visit('/login')

    cy.get('input[name=email]').type('admin@invoice.com')
    cy.get('input[name=password]').type('Admin123!')

    cy.get('button[type="submit"]').click()

    // wait until login redirect completes
    cy.location('pathname', { timeout: 10000 })
      .should('include', '/dashboard')

  })

  it('should display invoices table', () => {

    cy.visit('/invoices')

    cy.contains('Invoices').should('be.visible')

    cy.get('table').should('exist')

  })

})