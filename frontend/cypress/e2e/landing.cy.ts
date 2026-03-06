describe('Landing Page', () => {

  it('should load landing page', () => {

    cy.visit('/')

    cy.contains('Automated Invoice Insights')
    cy.contains('Upload files. Extract insights.')

    cy.contains('Login').should('exist')
    cy.contains('SignUp').should('exist')

  })

  it('should navigate to login', () => {

    cy.visit('/')

    cy.contains('Login').click()

    cy.url().should('include', '/login')

  })

  it('should navigate to signup', () => {

    cy.visit('/')

    cy.contains('SignUp').click()

    cy.url().should('include', '/signup')

  })

})