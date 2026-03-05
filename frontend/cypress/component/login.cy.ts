import { Login } from '../../src/app/features/auth/login/login'
import { provideHttpClient } from '@angular/common/http'
import { provideRouter } from '@angular/router'
import { Auth } from '../../src/app/core/services/auth'

describe('Login Component', () => {

  const mountLogin = () => {
    cy.mount(Login, {
      providers: [
        provideHttpClient(),
        provideRouter([])
      ]
    })
  }

  it('renders login UI', () => {
    mountLogin()
    cy.contains('Welcome Back')
    cy.contains('Sign in to continue')
    cy.contains('Login')
    cy.contains("Don't have an account?")
    cy.contains('Create one')
  })

  it('shows error when submitting empty form', () => {
    mountLogin()
    cy.get('button[type=submit]').click()
    cy.contains('Email and password are required')
  })

  it('shows error when only email is filled', () => {
    mountLogin()
    cy.get('input[type=email]').type('test@example.com')
    cy.get('button[type=submit]').click()
    cy.contains('Email and password are required')
  })

  it('toggles password visibility', () => {
    mountLogin()
    cy.get('input[name=password]').should('have.attr', 'type', 'password')
    cy.get('.eye-btn').click()
    cy.get('input[name=password]').should('have.attr', 'type', 'text')
    cy.get('.eye-btn').click()
    cy.get('input[name=password]').should('have.attr', 'type', 'password')
  })

  it('shows loading state while signing in', () => {
    cy.intercept('POST', '**/auth/login', { delay: 1000, statusCode: 200, body: { accessToken: 'tok' } }).as('login')
    mountLogin()
    cy.get('input[type=email]').type('user@example.com')
    cy.get('input[name=password]').type('password123')
    cy.get('button[type=submit]').click()
    cy.get('button[type=submit]').should('contain', 'Signing in…')
    cy.get('button[type=submit]').should('be.disabled')
  })

  it('shows 401 error for wrong credentials', () => {
    cy.intercept('POST', '**/auth/login', { statusCode: 401, body: {} }).as('login')
    mountLogin()
    cy.get('input[type=email]').type('wrong@example.com')
    cy.get('input[name=password]').type('wrongpass')
    cy.get('button[type=submit]').click()
    cy.wait('@login')
    cy.contains('Invalid email or password')
  })

  it('shows 403 error for unapproved account', () => {
    cy.intercept('POST', '**/auth/login', { statusCode: 403, body: {} }).as('login')
    mountLogin()
    cy.get('input[type=email]').type('pending@example.com')
    cy.get('input[name=password]').type('password123')
    cy.get('button[type=submit]').click()
    cy.wait('@login')
    cy.contains('Your account has not been approved yet')
  })

  it('shows 429 error for too many attempts', () => {
    cy.intercept('POST', '**/auth/login', {
      statusCode: 429,
      body: { message: 'Too many login attempts. Please try again later.' }
    }).as('login')
    mountLogin()
    cy.get('input[type=email]').type('user@example.com')
    cy.get('input[name=password]').type('password123')
    cy.get('button[type=submit]').click()
    cy.wait('@login')
    cy.contains('Too many login attempts')
  })

  it('shows 400 error with server message', () => {
    cy.intercept('POST', '**/auth/login', {
      statusCode: 400,
      body: { message: 'Account is locked' }
    }).as('login')
    mountLogin()
    cy.get('input[type=email]').type('user@example.com')
    cy.get('input[name=password]').type('password123')
    cy.get('button[type=submit]').click()
    cy.wait('@login')
    cy.contains('Account is locked')
  })

  it('shows generic error for unexpected failures', () => {
    cy.intercept('POST', '**/auth/login', { statusCode: 500, body: {} }).as('login')
    mountLogin()
    cy.get('input[type=email]').type('user@example.com')
    cy.get('input[name=password]').type('password123')
    cy.get('button[type=submit]').click()
    cy.wait('@login')
    cy.contains('An unexpected error occurred')
  })

  it('error banner is not shown on initial render', () => {
    mountLogin()
    cy.get('.error-banner').should('not.exist')
  })

  it('navigates to signup page via link', () => {
    mountLogin()
    cy.contains('Create one').should('have.attr', 'href', '/signup')
  })
})