## The HoldFast data source for Grafana
The HoldFast data source plugin allows you to query, visualize, and alert on metrics derived from HoldFast traces, logs, errors, and sessions.

## Requirements
There are no requirements or dependencies to test the HoldFast data source in our demo environment.

To use this data source for your cloud-managed project, you must be an enterprise customer. If you're interested in getting this set up, please reach out to us at [support@holdfast.local](mailto:support@holdfast.local).

If you're already self-hosting HoldFast with a [hobby](http://localhost:3000/docs/general/company/open-source/hosting/self-host-hobby) or [enterprise](http://localhost:3000/docs/general/company/open-source/hosting/self-host-enterprise) deployment, you're good to go. If you'd like to set up your own self-hosted instance, you can follow the [getting started guide](http://localhost:3000/docs/getting-started/self-host/self-hosted-hobby-guide).

## Getting Started with the HoldFast data source
### Configuration
![Configuration](https://raw.githubusercontent.com/highlight/highlight/main/sdk/highlightinc-highlight-datasource/src/img/configuration.png)

Once you have added a HoldFast data source, you can configure it with these fields:

|Name|Description|
|----|-----------|
|`HoldFast Backend URL`|The URL used to query HoldFast data. If you are using a self-hosted HoldFast instance, set this to `http://localhost:8082`.|
|`OAuth Token URL`|The URL used to retrieve an OAuth access token. If you are using a self-hosted HoldFast instance, set this to `http://localhost:8082/oauth/token`.|
|`Project ID`|The HoldFast project ID, accessible from the URL slug of your HoldFast project, e.g. `http://localhost:3000/{project_id}/`. If you want to query data from multiple projects, you can set up multiple data sources.|
|`Client ID`|If you are using a cloud-managed HoldFast instance, set this to your OAuth client ID. If you are setting up the data source for the first time and need a client ID, reach out to your administrator. If you are self-hosting HoldFast or using the demo project, leave this blank.|
|`Client Secret`|If you are using a cloud-managed HoldFast instance, set this to your OAuth client secret. If you are setting up the data source for the first time and need a client secret, reach out to your administrator. If you are self-hosting HoldFast or using the demo project, leave this blank.|

After entering these fields, clicking `Save & test` will run a health check to verify that the data source is able to query HoldFast data.

### Building your first query
The HoldFast data source query editor lets you interactively build a query to search and visualize your metrics from HoldFast traces, logs, errors, and sessions. To get started, add a new dashboard visualization or alert rule with the HoldFast data source.

The query editor is configurable with these fields:
|Name|Description|
|----|-----------|
|`Resource`|The HoldFast resource being queried for these metrics.|
|`Function`|The method used for aggregating data within a group and bucket, e.g. count, avg, p50. If the function operates on a metric, numeric fields from the linked project's data are shown as search suggestions.|
|`Filter`|A HoldFast filtering expression can be used to include only matching resources. Follows the search syntax shown [here](http://localhost:3000/docs/general/product-features/general-features/search).|
|`Group by`|One or more categories to group the results by. Categorical fields from the linked project's data are shown as search suggestions.|
|`Limit N`|If one or more "group by" categories are selected, the result groups are limited to the top N.|
|`Limit by function`|If one or more "group by" categories are selected, this method is used to rank categories before returning the top N.|
|`Bucket by`|The dimension used for bucketing numeric data. Supports bucketing by timestamp, no bucketing, or a custom bucketing key for histogram visualizations.|
|`Buckets`|The number of buckets returned.|

## Documentation
The docs page for the HoldFast Grafana plugin is available [here](http://localhost:3000/docs/general/integrations/grafana-integration/overview).

If you have any further questions about HoldFast, you can visit our [docs](http://localhost:3000/docs) or reach out to your administrator.

## Contributing
Looking to contribute to HoldFast? Get started [here](https://github.com/highlight/highlight/blob/main/docs-content/general/4_company/open-source/contributing/1_getting-started.md).
