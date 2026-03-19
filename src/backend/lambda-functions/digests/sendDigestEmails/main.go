package main

import (
	"github.com/aws/aws-lambda-go/lambda"
	lambdafunctions "github.com/BrewingCoder/holdfast/src/backend/lambda-functions"
	"github.com/BrewingCoder/holdfast/src/backend/lambda-functions/digests/handlers"
	"github.com/BrewingCoder/holdfast/sdk/highlight-go"
)

var h handlers.Handlers

func init() {
	h = handlers.NewHandlers()
}

func main() {
	lambdafunctions.Monitor("lambda-functions--sendDigestEmails")
	lambda.StartWithOptions(
		h.SendDigestEmails,
		lambda.WithEnableSIGTERM(highlight.Stop),
	)
}
