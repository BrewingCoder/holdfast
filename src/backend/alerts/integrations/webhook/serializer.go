package webhook

import (
	"github.com/BrewingCoder/holdfast/src/backend/model"
	modelInputs "github.com/BrewingCoder/holdfast/src/backend/private-graph/graph/model"
)

func GQLInputToGo(webhooks []*modelInputs.WebhookDestinationInput) []*model.WebhookDestination {
	var ret []*model.WebhookDestination
	for _, wh := range webhooks {
		ret = append(ret, &model.WebhookDestination{
			URL:           wh.URL,
			Authorization: wh.Authorization,
		})
	}

	return ret
}
