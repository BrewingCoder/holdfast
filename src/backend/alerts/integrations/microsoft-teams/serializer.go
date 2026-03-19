package microsoft_teams

import (
	"github.com/BrewingCoder/holdfast/src/backend/model"
	modelInputs "github.com/BrewingCoder/holdfast/src/backend/private-graph/graph/model"
)

func GQLInputToGo(microsoftTeamsChannels []*modelInputs.MicrosoftTeamsChannelInput) []*model.MicrosoftTeamsChannel {
	ret := []*model.MicrosoftTeamsChannel{}
	for _, channel := range microsoftTeamsChannels {
		ret = append(ret, &model.MicrosoftTeamsChannel{
			ID:   channel.ID,
			Name: channel.Name,
		})
	}

	return ret
}
