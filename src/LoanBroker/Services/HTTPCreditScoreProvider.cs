using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ClientMessages;
using Microsoft.Extensions.Logging;

namespace LoanBroker.Services;

public class HTTPCreditScoreProvider(ILogger<HTTPCreditScoreProvider> logger) : ICreditScoreProvider
{
    readonly string? functionUrl = Environment.GetEnvironmentVariable("CREDIT_BUREAU_URL");
    readonly HttpClient httpClient = new();

    public async Task<int> Score(Prospect prospect, string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionUrl);

        var requestRecord = new ScoreRequest(prospect.SSN, requestId);

        logger.LogInformation($"Sending request to credit bureau via {functionUrl}");

        using var httpResponseMessage = await httpClient.PostAsJsonAsync(functionUrl, requestRecord);

        httpResponseMessage.EnsureSuccessStatusCode();

        var scoreResponse = await httpResponseMessage.Content.ReadFromJsonAsync<ScoreResponse>()
            ?? throw new InvalidOperationException("Credit bureau returned an empty response");

        return scoreResponse.Score;
    }
}

public record ScoreRequest(
    [property: JsonPropertyName("ssn")] string SSN,
    [property: JsonPropertyName("requestId")] string RequestId);

public record ScoreResponse(
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("history")] int History,
    [property: JsonPropertyName("SSN")] string SSN,
    [property: JsonPropertyName("request_id")] string RequestId);
