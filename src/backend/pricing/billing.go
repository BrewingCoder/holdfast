package pricing

// Client is a no-op billing client for self-hosted deployments.
// All Stripe functionality has been removed.
type Client struct{}

func NewNoopClient() *Client {
	return &Client{}
}
