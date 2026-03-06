import { defineConfig } from "cypress";

export default defineConfig({
  allowCypressEnv: false,

  component: {
    devServer: {
      framework: "angular",
      bundler: "webpack",
    },
    specPattern: "cypress/component/**/*.cy.ts",
  },

  e2e: {
    baseUrl: "http://localhost:4200",
    specPattern: "cypress/e2e/**/*.cy.ts",

    setupNodeEvents(on, config) {
      return config;
    },
  },
});