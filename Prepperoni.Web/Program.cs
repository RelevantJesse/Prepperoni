using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");

if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddHttpClient("pollinations", client =>
{
    client.BaseAddress = new Uri("https://text.pollinations.ai/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("question-generator", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapPost("/api/interview-questions", async (
    InterviewQuestionRequest request,
    IHttpClientFactory httpClientFactory,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var jobTitle = request.JobTitle?.Trim();

    if (string.IsNullOrWhiteSpace(jobTitle))
    {
        return Results.BadRequest(new ErrorResponse("Add a job title first. The question machine refuses to guess."));
    }

    if (jobTitle.Length > 80)
    {
        return Results.BadRequest(new ErrorResponse("Keep the job title under 80 characters so the prompt stays focused."));
    }

    var prompt = $$"""
        Create exactly 3 thoughtful interview questions for the job title "{{jobTitle}}".

        Context:
        - This is for an early-stage startup interview.
        - The interviewer wants practical signal: ownership, communication, ambiguity, judgment, and role-specific skill.
        - Keep the tone sharp, useful, and lightly playful without becoming gimmicky.
        - Do not include personal names, resumes, phone numbers, or private candidate data.

        Return only JSON in this shape:
        {
          "questions": [
            {
              "question": "Question text",
              "why": "One short sentence explaining what the interviewer learns."
            }
          ]
        }
        """;

    var model = "openai";
    var aiRequest = new
    {
        model,
        messages = new[]
        {
            new
            {
                role = "system",
                content = "You help startup founders design practical interview questions. You are concise, clear, and a little witty."
            },
            new
            {
                role = "user",
                content = prompt
            }
        }
    };

    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "openai");
    httpRequest.Content = new StringContent(JsonSerializer.Serialize(aiRequest), Encoding.UTF8, "application/json");

    try
    {
        using var aiResponse = await httpClientFactory
            .CreateClient("pollinations")
            .SendAsync(httpRequest, cancellationToken);

        var modelText = await aiResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!aiResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Pollinations returned {StatusCode}: {Body}", aiResponse.StatusCode, modelText);
            return Results.Problem("The AI provider did not return questions. Please try again.", statusCode: 502);
        }

        var questions = ParseQuestions(ExtractOpenAiText(modelText));

        if (questions.Count != 3)
        {
            logger.LogWarning("Expected 3 questions but parsed {Count}. Raw model text: {Text}", questions.Count, modelText);
            return Results.Problem("The AI returned an unexpected format. Try again with a shorter job title.", statusCode: 502);
        }

        return Results.Ok(new InterviewQuestionResponse(jobTitle, model, questions));
    }
    catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to generate interview questions.");
        return Results.Problem("Something went sideways while calling the AI API. Please try again.", statusCode: 500);
    }
}).RequireRateLimiting("question-generator");

app.Run();

static string ExtractOpenAiText(string responseBody)
{
    using var document = JsonDocument.Parse(responseBody);
    return document
        .RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString() ?? string.Empty;
}

static IReadOnlyList<InterviewQuestion> ParseQuestions(string modelText)
{
    var cleaned = Regex
        .Replace(modelText.Trim(), "^```(?:json)?|```$", string.Empty, RegexOptions.Multiline)
        .Trim();

    using var document = JsonDocument.Parse(cleaned);
    var questions = new List<InterviewQuestion>();

    foreach (var item in document.RootElement.GetProperty("questions").EnumerateArray())
    {
        var question = item.GetProperty("question").GetString()?.Trim();
        var why = item.GetProperty("why").GetString()?.Trim();

        if (!string.IsNullOrWhiteSpace(question) && !string.IsNullOrWhiteSpace(why))
        {
            questions.Add(new InterviewQuestion(question, why));
        }
    }

    return questions;
}

public sealed record InterviewQuestionRequest(string? JobTitle);

public sealed record InterviewQuestionResponse(
    string JobTitle,
    string Model,
    IReadOnlyList<InterviewQuestion> Questions);

public sealed record InterviewQuestion(string Question, string Why);

public sealed record ErrorResponse(string Message);
