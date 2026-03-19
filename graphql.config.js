module.exports = {
	projects: {
		backendPrivate: {
			schema: './src/backend/private-graph/graph/schema.graphqls',
		},
		backendPublic: {
			schema: './src/backend/public-graph/graph/schema.graphqls',
		},
		frontend: {
			schema: './src/backend/private-graph/graph/schema.graphqls',
			documents: './src/frontend/**/*.gql',
		},
	},
}
