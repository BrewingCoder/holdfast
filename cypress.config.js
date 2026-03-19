const { defineConfig } = require('cypress')

module.exports = defineConfig({
	e2e: {
		baseUrl: 'http://localhost:3000',
		pageLoadTimeout: 1200000,
		video: false,
		specPattern: 'tests/cypress/e2e/**/*.cy.{js,jsx,ts,tsx}',
		supportFile: 'tests/cypress/support/e2e.{js,jsx,ts,tsx}',
		fixturesFolder: 'tests/cypress/fixtures',
		screenshotsFolder: 'tests/cypress/screenshots',
		videosFolder: 'tests/cypress/videos',
		setupNodeEvents(on, config) {
			// implement node event listeners here
		},
	},
})
