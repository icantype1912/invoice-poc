describe('Job Queue Page', () => {

  beforeEach(() => {

    cy.visit('/login')

    cy.get('input[name=email]').type('admin@invoice.com')
    cy.get('input[name=password]').type('Admin123!')

    // click the form submit button
    cy.get('button[type="submit"]').click()

    // wait for redirect after login
    cy.location('pathname', { timeout: 10000 })
      .should('include', '/dashboard')

  })

  it('should display job queue', () => {

    cy.visit('/jobs')

    // confirm correct page loaded
    cy.location('pathname').should('eq', '/jobs')

    cy.contains('Job Queue').should('be.visible')

    cy.get('table').should('be.visible')

  })

})