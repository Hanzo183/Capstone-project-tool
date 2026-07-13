using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;

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
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Description = "Paste only the JWT access token. Swagger will add the Bearer prefix automatically."
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
var jwtSecret = builder.Configuration["Jwt:SigningKey"] ?? "capstone-review-tool-development-signing-key";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType = "role",
            NameClaimType = "name"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("EvaluationSubmitters", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Lecturer", "CouncilMember")));
    options.AddPolicy("EvaluationViewers", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Lecturer", "CouncilMember", "Student")));
    options.AddPolicy("ReportViewers", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Lecturer", "CouncilMember", "Student")));
    options.AddPolicy("RebuttalReviewers", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "CouncilMember")));
    options.AddPolicy("StudentsOrAdmin", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Student")));
});
builder.Services.AddDbContext<EvaluationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<IntegrationEventPublisher>();

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (EvaluationDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "evaluation", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapPost("/evaluations", async (CreateEvaluationRequest request, EvaluationDbContext db, IntegrationEventPublisher events) =>
{
    if (string.IsNullOrWhiteSpace(request.ProjectId) ||
        string.IsNullOrWhiteSpace(request.RoundId) ||
        string.IsNullOrWhiteSpace(request.EvaluatorId))
    {
        return Results.BadRequest(new { message = "Project, round, and evaluator are required." });
    }

    if (request.Score < 0 || request.Score > 10)
    {
        return Results.BadRequest(new { message = "Score must be between 0 and 10." });
    }

    if (!string.IsNullOrWhiteSpace(request.StudentId) && !IsValidStudentId(request.StudentId.Trim().ToUpperInvariant()))
    {
        return Results.BadRequest(new { message = "Student ID must start with 2 letters followed by 6 numbers, for example SE192706." });
    }

    var evaluation = new EvaluationItem
    {
        Id = ShortId.New("EVA"),
        ProjectId = request.ProjectId.Trim(),
        RoundId = request.RoundId.Trim(),
        EvaluatorId = request.EvaluatorId.Trim(),
        Score = request.Score,
        Feedback = request.Feedback?.Trim(),
        SubmittedAt = DateTime.UtcNow
    };

    db.Evaluations.Add(evaluation);
    await db.SaveChangesAsync();
    await events.PublishAsync("evaluation.completed", new
    {
        evaluation.Id,
        evaluation.ProjectId,
        evaluation.RoundId,
        evaluation.EvaluatorId,
        request.StudentId,
        evaluation.Score,
        evaluation.Feedback,
        evaluation.SubmittedAt
    });

    return Results.Accepted($"/evaluations/project/{request.ProjectId}", new
    {
        evaluation,
        @event = "evaluation.completed"
    });
}).RequireAuthorization("EvaluationSubmitters");

app.MapGet("/evaluations/project/{projectId}", async (string projectId, EvaluationDbContext db) =>
{
    var projectEvaluations = await db.Evaluations
        .AsNoTracking()
        .Where(item => item.ProjectId == projectId)
        .OrderByDescending(item => item.SubmittedAt)
        .ToListAsync();

    return Results.Ok(projectEvaluations);
}).RequireAuthorization("EvaluationViewers");

app.MapGet("/evaluations", async (EvaluationDbContext db) =>
{
    var evaluations = await db.Evaluations
        .AsNoTracking()
        .OrderByDescending(item => item.SubmittedAt)
        .ToListAsync();

    return Results.Ok(evaluations);
}).RequireAuthorization("EvaluationSubmitters");

app.MapPost("/rebuttals", async (CreateRebuttalRequest request, EvaluationDbContext db, IntegrationEventPublisher events) =>
{
    if (string.IsNullOrWhiteSpace(request.EvaluationId) ||
        string.IsNullOrWhiteSpace(request.StudentId) ||
        string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest(new { message = "Evaluation, student, and rebuttal content are required." });
    }

    var normalizedStudentId = request.StudentId.Trim().ToUpperInvariant();
    if (!IsValidStudentId(normalizedStudentId))
    {
        return Results.BadRequest(new { message = "Student ID must start with 2 letters followed by 6 numbers, for example SE192706." });
    }

    var evaluation = await db.Evaluations.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.EvaluationId);
    if (evaluation is null)
    {
        return Results.NotFound(new { message = "Evaluation was not found." });
    }

    var rebuttal = new RebuttalItem
    {
        Id = ShortId.New("REB"),
        EvaluationId = request.EvaluationId.Trim(),
        StudentId = normalizedStudentId,
        Content = request.Content.Trim(),
        Status = "Pending",
        SubmittedAt = DateTime.UtcNow
    };

    db.Rebuttals.Add(rebuttal);
    await db.SaveChangesAsync();
    await events.PublishAsync("rebuttal.submitted", new
    {
        rebuttal.Id,
        rebuttal.EvaluationId,
        evaluation.ProjectId,
        evaluation.RoundId,
        evaluation.EvaluatorId,
        rebuttal.StudentId,
        rebuttal.Content,
        rebuttal.Status,
        rebuttal.SubmittedAt
    });

    return Results.Accepted($"/rebuttals/{rebuttal.Id}", new
    {
        rebuttal,
        @event = "rebuttal.submitted"
    });
}).RequireAuthorization("StudentsOrAdmin");

app.MapGet("/rebuttals", async (string? status, EvaluationDbContext db) =>
{
    var query = db.Rebuttals.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(item => item.Status == status);
    }

    var rebuttals = await query.OrderByDescending(item => item.SubmittedAt).ToListAsync();
    return Results.Ok(rebuttals);
}).RequireAuthorization("RebuttalReviewers");

app.MapGet("/rebuttals/evaluation/{evaluationId}", async (string evaluationId, EvaluationDbContext db) =>
{
    var rebuttals = await db.Rebuttals
        .AsNoTracking()
        .Where(item => item.EvaluationId == evaluationId)
        .OrderByDescending(item => item.SubmittedAt)
        .ToListAsync();

    return Results.Ok(rebuttals);
}).RequireAuthorization("EvaluationViewers");

app.MapPut("/rebuttals/{id}/status", async (string id, UpdateRebuttalStatusRequest request, EvaluationDbContext db, IntegrationEventPublisher events) =>
{
    if (!IsAllowedRebuttalStatus(request.Status))
    {
        return Results.BadRequest(new { message = "Status must be Pending, Approved, or Rejected." });
    }

    var rebuttal = await db.Rebuttals.FirstOrDefaultAsync(item => item.Id == id);
    if (rebuttal is null)
    {
        return Results.NotFound();
    }

    rebuttal.Status = NormalizeRebuttalStatus(request.Status);
    if (!string.IsNullOrWhiteSpace(request.Response))
    {
        rebuttal.Response = request.Response.Trim();
    }

    rebuttal.ReviewedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    if (rebuttal.Status != "Pending")
    {
        await events.PublishAsync("rebuttal.reviewed", new
        {
            rebuttal.Id,
            rebuttal.EvaluationId,
            rebuttal.StudentId,
            rebuttal.Status,
            rebuttal.Response,
            rebuttal.ReviewedAt
        });
    }

    return Results.Ok(rebuttal);
}).RequireAuthorization("RebuttalReviewers");

app.MapPut("/rebuttals/{id}/respond", async (string id, RespondToRebuttalRequest request, EvaluationDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Response))
    {
        return Results.BadRequest(new { message = "Response is required." });
    }

    var rebuttal = await db.Rebuttals.FirstOrDefaultAsync(item => item.Id == id);
    if (rebuttal is null)
    {
        return Results.NotFound();
    }

    rebuttal.Response = request.Response.Trim();
    rebuttal.ReviewedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(rebuttal);
}).RequireAuthorization("RebuttalReviewers");

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
}).RequireAuthorization("ReportViewers");

app.MapGet("/reports/{roundId}/pdf", async (string roundId, EvaluationDbContext db) =>
{
    var roundEvaluations = await db.Evaluations
        .AsNoTracking()
        .Where(item => item.RoundId == roundId)
        .OrderByDescending(item => item.Score)
        .ToListAsync();
    var report = new RoundReport(
        roundId,
        roundEvaluations.Count,
        roundEvaluations.Count == 0 ? 0 : Math.Round(roundEvaluations.Average(item => item.Score), 2),
        roundEvaluations);
    var pdfBytes = PdfReportBuilder.Build(report);

    return Results.File(pdfBytes, "application/pdf", $"round-{roundId}-report.pdf");
}).RequireAuthorization("ReportViewers");

app.Run();

static bool HasRole(ClaimsPrincipal user, params string[] roles)
{
    var roleClaims = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role));
    return roleClaims.Any(claim => roles.Any(role => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
}

static bool IsValidStudentId(string studentId) =>
    Regex.IsMatch(studentId, "^[A-Z]{2}\\d{6}$");

static bool IsAllowedRebuttalStatus(string status) => !string.IsNullOrWhiteSpace(NormalizeRebuttalStatus(status));

static string NormalizeRebuttalStatus(string status)
{
    string[] allowedStatuses = ["Pending", "Approved", "Rejected"];
    return allowedStatuses.FirstOrDefault(allowedStatus =>
        string.Equals(allowedStatus, status?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
}

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

sealed class IntegrationEventPublisher(IConfiguration configuration, ILogger<IntegrationEventPublisher> logger)
{
    private readonly string exchangeName = configuration["RabbitMQ:ExchangeName"] ?? "capstone.events";

    public Task PublishAsync(string eventName, object payload)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = configuration["RabbitMQ:HostName"] ?? "localhost",
                Port = int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672,
                UserName = configuration["RabbitMQ:UserName"] ?? "guest",
                Password = configuration["RabbitMQ:Password"] ?? "guest",
                DispatchConsumersAsync = true
            };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();
            channel.ExchangeDeclare(exchangeName, ExchangeType.Topic, durable: true, autoDelete: false);

            var envelope = JsonSerializer.SerializeToUtf8Bytes(new IntegrationEvent(eventName, DateTime.UtcNow, payload));
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.Type = eventName;

            channel.BasicPublish(exchangeName, eventName, properties, envelope);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not publish integration event {EventName}.", eventName);
        }

        return Task.CompletedTask;
    }
}

static class PdfReportBuilder
{
    public static byte[] Build(RoundReport report)
    {
        var lines = new List<string>
        {
            "Capstone Review Round Report",
            $"Round: {report.RoundId}",
            $"Evaluations: {report.EvaluationCount}",
            $"Average score: {report.AverageScore:0.00}",
            "",
            "Project | Evaluator | Score | Feedback"
        };

        lines.AddRange(report.Evaluations.Select(item =>
            $"{item.ProjectId} | {item.EvaluatorId} | {item.Score:0.00} | {item.Feedback ?? "No feedback"}"));

        var content = new StringBuilder();
        content.AppendLine("BT");
        content.AppendLine("/F1 11 Tf");
        content.AppendLine("50 780 Td");
        content.AppendLine("14 TL");
        foreach (var line in lines.Take(48))
        {
            content.Append('(').Append(EscapePdfText(line)).AppendLine(") Tj");
            content.AppendLine("T*");
        }
        content.AppendLine("ET");
        var stream = Encoding.ASCII.GetBytes(content.ToString());

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {stream.Length} >>\nstream\n{Encoding.ASCII.GetString(stream)}endstream"
        };

        var pdf = new StringBuilder();
        pdf.AppendLine("%PDF-1.4");
        var offsets = new List<int> { 0 };
        foreach (var (obj, index) in objects.Select((value, index) => (value, index)))
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.AppendLine($"{index + 1} 0 obj");
            pdf.AppendLine(obj);
            pdf.AppendLine("endobj");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.AppendLine("xref");
        pdf.AppendLine($"0 {objects.Count + 1}");
        pdf.AppendLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1))
        {
            pdf.AppendLine($"{offset:0000000000} 00000 n ");
        }

        pdf.AppendLine("trailer");
        pdf.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        pdf.AppendLine("startxref");
        pdf.AppendLine(xrefOffset.ToString());
        pdf.AppendLine("%%EOF");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string EscapePdfText(string value) =>
        value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

record IntegrationEvent(string Name, DateTime OccurredAt, object Payload);

record RoundReport(string RoundId, int EvaluationCount, decimal AverageScore, IEnumerable<EvaluationItem> Evaluations);
record CreateEvaluationRequest(string ProjectId, string RoundId, string EvaluatorId, decimal Score, string? Feedback, string? StudentId);
record CreateRebuttalRequest(string EvaluationId, string StudentId, string Content);
record UpdateRebuttalStatusRequest(string Status, string? Response);
record RespondToRebuttalRequest(string Response);
