using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Loads;

/// <summary>
/// Configuration for the webhook load node
/// </summary>
[NodeName("ToWebhook", 1)]
public record ToWebhookNodeConfiguration : PathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the target URI for the webhook
    /// </summary>
    [PropertyGroup("Connection", 0)]
    public required string Uri { get; set; }

    /// <summary>
    /// Gets or sets the optional API key sent as "XApiKey" header
    /// </summary>
    [PropertyGroup("Connection", 1)]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the HTTP timeout in seconds
    /// </summary>
    [PropertyGroup("Timing", 0)]
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Sends pipeline data to an HTTP endpoint via POST request
/// </summary>
[NodeConfiguration(typeof(ToWebhookNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class ToWebhookNode(NodeDelegate next, IHttpClientFactory httpClientFactory) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<ToWebhookNodeConfiguration>();

        var payload = dataContext.Get<JsonNode>(c.Path);
        var json = payload != null
            ? JsonSerializer.Serialize(payload, SystemTextJsonOptions.Default)
            : "null";

        var client = httpClientFactory.CreateClient("Webhook");
        client.Timeout = TimeSpan.FromSeconds(c.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, c.Uri);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (!string.IsNullOrEmpty(c.ApiKey))
        {
            request.Headers.Add("XApiKey", c.ApiKey);
        }

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await next(dataContext, nodeContext);
    }
}
