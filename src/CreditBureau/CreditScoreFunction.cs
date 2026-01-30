using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CreditBureau;

public class CreditScoreFunction
{
    public CreditScoreFunction(ILogger<CreditScoreFunction> logger)
    {
        _logger = logger;
    }

    [Function("score")]
    public async Task<IResult> Score(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req,
        FunctionContext context)
    {
        var scoreRequest = await req.ReadFromJsonAsync<ScoreRequest>();
        _logger.LogInformation("Received score request: {RequestBody}", JsonSerializer.Serialize(scoreRequest));

        if (scoreRequest is null || string.IsNullOrEmpty(scoreRequest.SSN))
        {
            return Results.BadRequest("Invalid request body or missing SSN");
        }

        if (!SsnRegex.IsMatch(scoreRequest.SSN))
        {
            return Results.BadRequest("Invalid SSN format");
        }

        return Results.Ok(new ScoreResponse(
            GetRandomInt(MinScore, MaxScore),
            GetRandomInt(1, 30),
            scoreRequest.SSN,
            scoreRequest.RequestId));
    }

    private static int GetRandomInt(int min, int max)
    {
        return min + Random.Shared.Next(max - min);
    }

    private readonly ILogger<CreditScoreFunction> _logger;
    private const int MinScore = 300;
    private const int MaxScore = 900;
    private static readonly Regex SsnRegex = new(@"^\d{3}-\d{2}-\d{4}$", RegexOptions.Compiled);
}

public record ScoreRequest(
    [property: JsonPropertyName("ssn")] string SSN,
    [property: JsonPropertyName("requestId")] string RequestId);

public record ScoreResponse(
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("history")] int History,
    [property: JsonPropertyName("SSN")] string SSN,
    [property: JsonPropertyName("request_id")] string RequestId);