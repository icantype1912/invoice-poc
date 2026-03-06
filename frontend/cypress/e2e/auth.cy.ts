describe('Authentication', () => {

  it('should login successfully', () => {

    cy.visit('/login')

    cy.get('input[name=email]').type('admin@invoice.com')
    cy.get('input[name=password]').type('Admin123!')

    // click the form submit button
    cy.get('button[type="submit"]').click()

    // wait for redirect to dashboard
    cy.location('pathname', { timeout: 10000 })
      .should('include', '/dashboard')

  })

  it('should redirect unauthenticated users from dashboard', () => {

    cy.visit('/dashboard')

    cy.location('pathname')
      .should('include', '/login')

  })

})