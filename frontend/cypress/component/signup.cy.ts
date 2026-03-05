import { Signup } from '../../src/app/features/auth/signup/signup'
import { provideHttpClient } from '@angular/common/http'
import { provideRouter } from '@angular/router'

describe('Signup Component', () => {

  const mountSignup = () => {
    cy.mount(Signup, {
      providers: [
        provideHttpClient(),
        provideRouter([])
      ]
    })
  }

  // ── UI Rendering ──────────────────────────────────────────────
  it('renders signup UI', () => {
    mountSignup()
    cy.contains('Create Account')
    cy.contains('Start turning documents into intelligence')
    cy.contains('Sign Up')
    cy.contains('Already have an account?')
    cy.contains('Login')
  })

  it('does not show error or success banners on initial render', () => {
    mountSignup()
    cy.get('.error-banner').should('not.exist')
    cy.get('.success-banner').should('not.exist')
  })

  it('shows login link pointing to /login', () => {
    mountSignup()
    cy.contains('Login').should('have.attr', 'href', '/login')
  })

  // ── Validation ────────────────────────────────────────────────
  it('shows error when all fields are empty', () => {
    mountSignup()
    cy.get('button[type=submit]').click()
    cy.contains('Email, password, and company name are required')
  })

  it('shows error when email and password are filled but company is missing', () => {
    mountSignup()
    cy.get('input[name=signup-email]').type('user@example.com')
    cy.get('input[name=new-password]').type('Password1!')
    cy.get('button[type=submit]').click()
    cy.contains('Email, password, and company name are required')
  })

  it('shows error when passwords do not match', () => {
    mountSignup()
    cy.get('input[name=signup-email]').type('user@example.com')
    cy.get('input[name=company]').type('ACME Corp')
    cy.get('input[name=new-password]').type('Password1!')
    cy.get('input[name=confirm-new-password]').type('Different1!')
    cy.get('button[type=submit]').click()
    cy.contains('Passwords do not match')
  })

  it('shows error when password is less than 8 characters', () => {
    mountSignup()
    cy.get('input[name=signup-email]').type('user@example.com')
    cy.get('input[name=company]').type('ACME Corp')
    cy.get('input[name=new-password]').type('Ab1!')
    cy.get('input[name=confirm-new-password]').type('Ab1!')
    cy.get('button[type=submit]').click()
    cy.contains('Password must be at least 8 characters')
  })

  it('shows error when password has no lowercase', () => {
    mountSignup()
    cy.get('input[name=signup-email]').type('user@example.com')
    cy.get('input[name=company]').type('ACME Corp')
    cy.get('input[name=new-password]').type('PASSWORD1!')
    cy.get('input[name=confirm-new-password]').type('PASSWORD1!')
    cy.get('button[type=submit]').click()
    cy.contains('Password must include at least one lowercase character')
  })

  it('shows error when password has no uppercase', () => {
    mountSignup()
    cy.get('input[name=signup-email]').type('user@example.com')
    cy.get('input[name=company]').type('ACME Corp')
    cy.get('input[name=new-password]').type('password1!')
    cy.get('input[name=confirm-new-password]').type('password1!')
    cy.get('button[type=submit]').click()
    cy.contains('Password must include at least one uppercase character')
  })

  it('shows error when password has no digit', () => {
    mountSignup()
    cy.get('input[name=signup-email]').type('user@example.com')
    cy.get('input[name=company]').type('ACME Corp')
    cy.get('input[name=new-password]').type('Password!')
    cy.get('input[name=confirm-new-password]').type('Password!')
    cy.get('button[type=submit]').click()
    cy.contains('Password must include at least one number')
  })

  it('shows error when password has no special character', () => {
    mountSignup()
    cy.get('input[name=signup-email]').type('user@example.com')
    cy.get('input[name=company]').type('ACME Corp')
    cy.get('input[name=new-password]').type('Password1')
    cy.get('input[name=confirm-new-password]').type('Password1')
    cy.get('button[type=submit]').click()
    cy.contains('Password must include at least one special character')
  })

  // ── Password Toggle ───────────────────────────────────────────
  it('toggles password field visibility', () => {
    mountSignup()
    cy.get('input[name=new-password]').should('have.attr', 'type', 'password')
    cy.get('.eye-btn').first().click()
    cy.get('input[name=new-password]').should('have.attr', 'type', 'text')
    cy.get('.eye-btn').first().click()
    cy.get('input[name=new-password]').should('have.attr', 'type', 'password')
  })

  it('toggles confirm password field visibility', () => {
    mountSignup()
    cy.get('input[name=confirm-new-password]').should('have.attr', 'type', 'password')
    cy.get('.eye-btn').last().click()
    cy.get('input[name=confirm-new-password]').should('have.attr', 'type', 'text')
    cy.get('.eye-btn').last().click()
    cy.get('input[name=confirm-new-password]').should('have.attr', 'type', 'password')
  })

  // ── API responses ─────────────────────────────────────────────
  const fillValidForm = () => {
    cy.get('input[name=signup-email]').type('user@example.com')
    cy.get('input[name=company]').type('ACME Corp')
    cy.get('input[name=new-password]').type('Password1!')
    cy.get('input[name=confirm-new-password]').type('Password1!')
    cy.get('button[type=submit]').click()
  }

  it('shows loading state while submitting', () => {
    cy.intercept('POST', '**/auth/signup', { delay: 1000, statusCode: 200, body: {} }).as('signup')
    mountSignup()
    fillValidForm()
    cy.get('button[type=submit]').should('contain', 'Creating account…')
    cy.get('button[type=submit]').should('be.disabled')
  })

  it('shows success message and hides form on successful signup', () => {
    cy.intercept('POST', '**/auth/signup', { statusCode: 200, body: {} }).as('signup')
    mountSignup()
    fillValidForm()
    cy.wait('@signup')
    cy.contains('Signup successful!')
    cy.contains('pending approval')
    cy.get('form').should('not.exist')
  })

  it('shows error for prohibited registration', () => {
    cy.intercept('POST', '**/auth/signup', {
      statusCode: 400,
      body: { message: 'Prohibited to register contact admin' }
    }).as('signup')
    mountSignup()
    fillValidForm()
    cy.wait('@signup')
    cy.contains('Prohibited to register contact admin')
  })

  it('shows error for 400 with server message', () => {
    cy.intercept('POST', '**/auth/signup', {
      statusCode: 400,
      body: { message: 'Email already in use' }
    }).as('signup')
    mountSignup()
    fillValidForm()
    cy.wait('@signup')
    cy.contains('Email already in use')
  })

  it('shows fallback error for 400 without message', () => {
    cy.intercept('POST', '**/auth/signup', { statusCode: 400, body: {} }).as('signup')
    mountSignup()
    fillValidForm()
    cy.wait('@signup')
    cy.contains('Invalid signup data. Please check your inputs.')
  })

  it('shows error for 429 too many attempts', () => {
    cy.intercept('POST', '**/auth/signup', {
      statusCode: 429,
      body: { message: 'Too many signup attempts. Please try again later.' }
    }).as('signup')
    mountSignup()
    fillValidForm()
    cy.wait('@signup')
    cy.contains('Too many signup attempts')
  })

  it('shows generic error for unexpected failures', () => {
    cy.intercept('POST', '**/auth/signup', { statusCode: 500, body: {} }).as('signup')
    mountSignup()
    fillValidForm()
    cy.wait('@signup')
    cy.contains('An unexpected error occurred')
  })
})