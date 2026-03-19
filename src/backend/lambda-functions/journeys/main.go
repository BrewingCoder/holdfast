package main

import (
	"context"

	"github.com/BrewingCoder/holdfast/src/backend/lambda-functions/journeys/handlers"
	"github.com/BrewingCoder/holdfast/src/backend/lambda-functions/journeys/utils"
	log "github.com/sirupsen/logrus"
)

func main() {
	ctx := context.TODO()

	h := handlers.NewHandlers()
	sessions, err := h.GetSessions(ctx, 1)
	if err != nil {
		log.WithContext(ctx).Fatal(err)
	}

	for _, s := range sessions {
		resp, err := h.GetJourney(ctx, utils.JourneyInput{
			ProjectID: 1,
			SessionID: s,
		})
		if err != nil {
			log.WithContext(ctx).Fatal(err)
		}
		log.WithContext(ctx).Infof("%#v", resp)
	}
}
