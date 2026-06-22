var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

var projectId = Guid.NewGuid();
var roundId = Guid.NewGuid();
var evaluations = new List<EvaluationItem>
{
    new(Guid.NewGuid(), projectId, roundId, "CM001", 8.5m, "Architecture is clear; add stronger testing evidence.", DateTimeOffset.UtcNow.AddHours(-8))
};
var rebuttals = new List<RebuttalItem>();

app.MapGet("/health", () => Results.Ok(new { service = "evaluation", status = "healthy" }));

app.MapPost("/evaluations", (CreateEvaluationRequest request) =>
{
    var evaluation = new EvaluationItem(
        Guid.NewGuid(),
        request.ProjectId,
        request.RoundId,
        request.EvaluatorId,
        request.Score,
        request.Feedback,
        DateTimeOffset.UtcNow);

    evaluations.Add(evaluation);

    return Results.Accepted($"/evaluations/project/{request.ProjectId}", new
    {
        evaluation,
        @event = "evaluation.completed"
    });
});

app.MapGet("/evaluations/project/{projectId:guid}", (Guid projectId) =>
{
    var projectEvaluations = evaluations.Where(item => item.ProjectId == projectId).OrderByDescending(item => item.SubmittedAt);
    return Results.Ok(projectEvaluations);
});

app.MapGet("/evaluations", () => Results.Ok(evaluations.OrderByDescending(item => item.SubmittedAt)));

app.MapPost("/rebuttals", (CreateRebuttalRequest request) =>
{
    if (evaluations.All(item => item.Id != request.EvaluationId))
    {
        return Results.NotFound(new { message = "Evaluation was not found." });
    }

    var rebuttal = new RebuttalItem(Guid.NewGuid(), request.EvaluationId, request.StudentId, request.Content, "Pending", null, DateTimeOffset.UtcNow);
    rebuttals.Add(rebuttal);

    return Results.Accepted($"/rebuttals/{rebuttal.Id}", new
    {
        rebuttal,
        @event = "rebuttal.submitted"
    });
});

app.MapGet("/rebuttals", (string? status) =>
{
    var query = rebuttals.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.OrderByDescending(item => item.SubmittedAt));
});

app.MapPut("/rebuttals/{id:guid}/status", (Guid id, UpdateRebuttalStatusRequest request) =>
{
    var index = rebuttals.FindIndex(item => item.Id == id);
    if (index < 0)
    {
        return Results.NotFound();
    }

    rebuttals[index] = rebuttals[index] with { Status = request.Status, ReviewedAt = DateTimeOffset.UtcNow };
    return Results.Ok(rebuttals[index]);
});

app.MapGet("/reports/{roundId:guid}", (Guid roundId) =>
{
    var roundEvaluations = evaluations.Where(item => item.RoundId == roundId).ToList();
    return Results.Ok(new RoundReport(
        roundId,
        roundEvaluations.Count,
        roundEvaluations.Count == 0 ? 0 : Math.Round(roundEvaluations.Average(item => item.Score), 2),
        roundEvaluations.OrderByDescending(item => item.Score)));
});

app.Run();

record EvaluationItem(Guid Id, Guid ProjectId, Guid RoundId, string EvaluatorId, decimal Score, string Feedback, DateTimeOffset SubmittedAt);
record RebuttalItem(Guid Id, Guid EvaluationId, string StudentId, string Content, string Status, DateTimeOffset? ReviewedAt, DateTimeOffset SubmittedAt);
record RoundReport(Guid RoundId, int EvaluationCount, decimal AverageScore, IEnumerable<EvaluationItem> Evaluations);
record CreateEvaluationRequest(Guid ProjectId, Guid RoundId, string EvaluatorId, decimal Score, string Feedback);
record CreateRebuttalRequest(Guid EvaluationId, string StudentId, string Content);
record UpdateRebuttalStatusRequest(string Status);
