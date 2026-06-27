using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\""
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddDbContext<EvaluationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", async (EvaluationDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "evaluation", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapPost("/evaluations", async (CreateEvaluationRequest request, EvaluationDbContext db) =>
{
    var evaluation = new EvaluationItem
    {
        Id = ShortId.New("EVA"),
        ProjectId = request.ProjectId,
        RoundId = request.RoundId,
        EvaluatorId = request.EvaluatorId,
        Score = request.Score,
        Feedback = request.Feedback,
        SubmittedAt = DateTime.UtcNow
    };

    db.Evaluations.Add(evaluation);
    await db.SaveChangesAsync();

    return Results.Accepted($"/evaluations/project/{request.ProjectId}", new
    {
        evaluation,
        @event = "evaluation.completed"
    });
});

app.MapGet("/evaluations/project/{projectId}", async (string projectId, EvaluationDbContext db) =>
{
    var projectEvaluations = await db.Evaluations
        .AsNoTracking()
        .Where(item => item.ProjectId == projectId)
        .OrderByDescending(item => item.SubmittedAt)
        .ToListAsync();

    return Results.Ok(projectEvaluations);
});

app.MapGet("/evaluations", async (EvaluationDbContext db) =>
{
    var evaluations = await db.Evaluations
        .AsNoTracking()
        .OrderByDescending(item => item.SubmittedAt)
        .ToListAsync();

    return Results.Ok(evaluations);
});

app.MapPost("/rebuttals", async (CreateRebuttalRequest request, EvaluationDbContext db) =>
{
    var evaluationExists = await db.Evaluations.AnyAsync(item => item.Id == request.EvaluationId);
    if (!evaluationExists)
    {
        return Results.NotFound(new { message = "Evaluation was not found." });
    }

    var rebuttal = new RebuttalItem
    {
        Id = ShortId.New("REB"),
        EvaluationId = request.EvaluationId,
        StudentId = request.StudentId,
        Content = request.Content,
        Status = "Pending",
        SubmittedAt = DateTime.UtcNow
    };

    db.Rebuttals.Add(rebuttal);
    await db.SaveChangesAsync();

    return Results.Accepted($"/rebuttals/{rebuttal.Id}", new
    {
        rebuttal,
        @event = "rebuttal.submitted"
    });
});

app.MapGet("/rebuttals", async (string? status, EvaluationDbContext db) =>
{
    var query = db.Rebuttals.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(item => item.Status == status);
    }

    var rebuttals = await query.OrderByDescending(item => item.SubmittedAt).ToListAsync();
    return Results.Ok(rebuttals);
});

app.MapPut("/rebuttals/{id}/status", async (string id, UpdateRebuttalStatusRequest request, EvaluationDbContext db) =>
{
    var rebuttal = await db.Rebuttals.FirstOrDefaultAsync(item => item.Id == id);
    if (rebuttal is null)
    {
        return Results.NotFound();
    }

    rebuttal.Status = request.Status;
    rebuttal.Response = request.Response;
    rebuttal.ReviewedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(rebuttal);
});

app.MapGet("/reports/{roundId}", async (string roundId, EvaluationDbContext db) =>
{
    var roundEvaluations = await db.Evaluations
        .AsNoTracking()
        .Where(item => item.RoundId == roundId)
        .OrderByDescending(item => item.Score)
        .ToListAsync();

    return Results.Ok(new RoundReport(
        roundId,
        roundEvaluations.Count,
        roundEvaluations.Count == 0 ? 0 : Math.Round(roundEvaluations.Average(item => item.Score), 2),
        roundEvaluations));
});

app.Run();

sealed class EvaluationDbContext(DbContextOptions<EvaluationDbContext> options) : DbContext(options)
{
    public DbSet<EvaluationItem> Evaluations => Set<EvaluationItem>();
    public DbSet<RebuttalItem> Rebuttals => Set<RebuttalItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvaluationItem>().ToTable("Evaluations").HasKey(item => item.Id);
        modelBuilder.Entity<EvaluationItem>().Property(item => item.Score).HasPrecision(5, 2);
        modelBuilder.Entity<RebuttalItem>().ToTable("Rebuttals").HasKey(item => item.Id);
    }
}

sealed class EvaluationItem
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string RoundId { get; set; } = "";
    public string EvaluatorId { get; set; } = "";
    public decimal Score { get; set; }
    public string? Feedback { get; set; }
    public DateTime SubmittedAt { get; set; }
}

sealed class RebuttalItem
{
    public string Id { get; set; } = "";
    public string EvaluationId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Content { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? Response { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

static class ShortId
{
    public static string New(string prefix) => $"{prefix}-{RandomNumberGenerator.GetHexString(8)}";
}

record RoundReport(string RoundId, int EvaluationCount, decimal AverageScore, IEnumerable<EvaluationItem> Evaluations);
record CreateEvaluationRequest(string ProjectId, string RoundId, string EvaluatorId, decimal Score, string? Feedback);
record CreateRebuttalRequest(string EvaluationId, string StudentId, string Content);
record UpdateRebuttalStatusRequest(string Status, string? Response);
