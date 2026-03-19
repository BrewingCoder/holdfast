package discord

import (
	"github.com/BrewingCoder/holdfast/src/backend/model"
	modelInputs "github.com/BrewingCoder/holdfast/src/backend/private-graph/graph/model"
)

func GQLInputToGo(discordChannels []*modelInputs.DiscordChannelInput) []*model.DiscordChannel {
	ret := []*model.DiscordChannel{}
	for _, channel := range discordChannels {
		ret = append(ret, &model.DiscordChannel{
			ID:   channel.ID,
			Name: channel.Name,
		})
	}

	return ret
}
