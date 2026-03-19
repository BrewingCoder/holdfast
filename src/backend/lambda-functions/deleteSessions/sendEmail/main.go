package main

import (
	"github.com/BrewingCoder/holdfast/sdk/highlight-go"
	lambdafunctions "github.com/BrewingCoder/holdfast/src/backend/lambda-functions"
	"github.com/BrewingCoder/holdfast/src/backend/lambda-functions/deleteSessions/handlers"
	"github.com/aws/aws-lambda-go/lambda"
)

var h handlers.Handlers

func init() {
	h = handlers.NewHandlers()
}

func main() {
	lambdafunctions.Monitor("lambda-functions--deleteSessions-sendEmail")
	lambda.StartWithOptions(
		h.SendEmail,
		lambda.WithEnableSIGTERM(highlight.Stop),
	)
}
