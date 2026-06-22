var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

var projects = new List<ProjectItem>
{
    new(Guid.NewGuid(), "AI Review Scheduler", "Team 6", "SE192879", "In Review", "Spring 2025 Round 1", DateTimeOffset.UtcNow.AddDays(-12), DateTimeOffset.UtcNow.AddHours(-4)),
    new(Guid.NewGuid(), "Submission Quality Tracker", "Team 2", "SE192879", "Submitted", "Spring 2025 Round 1", DateTimeOffset.UtcNow.AddDays(-9), DateTimeOffset.UtcNow.AddDays(-1)),
    new(Guid.NewGuid(), "Council Scoring Portal", "Team 4", "SE192737", "Needs Revision", "Spring 2025 Round 1", DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow.AddHours(-10))
};

var submissions = new List<SubmissionItem>
{
    new(Guid.NewGuid(), projects[0].Id, "proposal-v1.pdf", "https://files.local/proposal-v1.pdf", 1, DateTimeOffset.UtcNow.AddDays(-5), "SE192706"),
    new(Guid.NewGuid(), projects[0].Id, "architecture-v2.pdf", "https://files.local/architecture-v2.pdf", 2, DateTimeOffset.UtcNow.AddDays(-1), "SE192706")
};

app.MapGet("/health", () => Results.Ok(new { service = "project", status = "healthy" }));

app.MapGet("/projects", (string? status, string? round, string? reviewer) =>
{
    var query = projects.AsEnumerable();

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(project => project.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(round))
    {
        query = query.Where(project => project.RoundId.Contains(round, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(reviewer))
    {
        query = query.Where(project => project.LecturerId.Equals(reviewer, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.OrderByDescending(project => project.UpdatedAt));
});

app.MapPost("/projects", (CreateProjectRequest request) =>
{
    var project = new ProjectItem(
        Guid.NewGuid(),
        request.Title,
        request.TeamId,
        request.LecturerId,
        "Draft",
        request.RoundId,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    projects.Add(project);
    return Results.Created($"/projects/{project.Id}", project);
});

app.MapGet("/projects/{id:guid}", (Guid id) =>
{
    var project = projects.FirstOrDefault(candidate => candidate.Id == id);
    return project is null ? Results.NotFound() : Results.Ok(project);
});

app.MapPost("/projects/{id:guid}/submit", (Guid id, SubmitProjectRequest request) =>
{
    var projectIndex = projects.FindIndex(candidate => candidate.Id == id);
    if (projectIndex < 0)
    {
        return Results.NotFound();
    }

    var nextVersion = submissions.Where(item => item.ProjectId == id).Select(item => item.Version).DefaultIfEmpty(0).Max() + 1;
    var submission = new SubmissionItem(Guid.NewGuid(), id, request.FileName, request.FileUrl, nextVersion, DateTimeOffset.UtcNow, request.SubmittedBy);
    submissions.Add(submission);

    var project = projects[projectIndex];
    projects[projectIndex] = project with { Status = "Submitted", UpdatedAt = DateTimeOffset.UtcNow };

    return Results.Accepted($"/projects/{id}/history", new
    {
        submission,
        @event = "project.submitted"
    });
});

app.MapGet("/projects/{id:guid}/history", (Guid id) =>
{
    var projectSubmissions = submissions.Where(item => item.ProjectId == id).OrderByDescending(item => item.Version);
    return Results.Ok(projectSubmissions);
});

app.MapPatch("/projects/{id:guid}/status", (Guid id, UpdateProjectStatusRequest request) =>
{
    var projectIndex = projects.FindIndex(candidate => candidate.Id == id);
    if (projectIndex < 0)
    {
        return Results.NotFound();
    }

    projects[projectIndex] = projects[projectIndex] with
    {
        Status = request.Status,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    return Results.Ok(projects[projectIndex]);
});

app.Run();

record ProjectItem(Guid Id, string Title, string TeamId, string LecturerId, string Status, string RoundId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
record SubmissionItem(Guid Id, Guid ProjectId, string FileName, string FileUrl, int Version, DateTimeOffset SubmittedAt, string SubmittedBy);
record CreateProjectRequest(string Title, string TeamId, string LecturerId, string RoundId);
record SubmitProjectRequest(string FileName, string FileUrl, string SubmittedBy);
record UpdateProjectStatusRequest(string Status);
