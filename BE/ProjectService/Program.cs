using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
    options.AddPolicy("ProjectManagers", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Lecturer")));
    options.AddPolicy("Submitters", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Student")));
});
builder.Services.AddDbContext<ProjectDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<IntegrationEventPublisher>();

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (ProjectDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "project", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapGet("/projects", async (string? status, string? round, string? reviewer, ProjectDbContext db) =>
{
    var query = db.Projects.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(project => project.Status == status);
    }

    if (!string.IsNullOrWhiteSpace(round))
    {
        query = query.Where(project => project.RoundId != null && project.RoundId.Contains(round));
    }

    if (!string.IsNullOrWhiteSpace(reviewer))
    {
        query = query.Where(project => project.LecturerId == reviewer);
    }

    var projects = await query.OrderByDescending(project => project.UpdatedAt).ToListAsync();
    return Results.Ok(projects);
}).RequireAuthorization("Authenticated");

app.MapPost("/projects", async (CreateProjectRequest request, ProjectDbContext db) =>
{
    var project = new ProjectItem
    {
        Id = ShortId.New("PRJ"),
        Title = request.Title,
        Description = request.Description,
        TeamId = request.TeamId,
        TeamLeaderId = request.TeamLeaderId,
        LecturerId = request.LecturerId,
        Status = "Draft",
        RoundId = request.RoundId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    db.Projects.Add(project);
    await db.SaveChangesAsync();

    return Results.Created($"/projects/{project.Id}", project);
}).RequireAuthorization("ProjectManagers");

app.MapGet("/projects/{id}", async (string id, ProjectDbContext db) =>
{
    var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    return project is null ? Results.NotFound() : Results.Ok(project);
}).RequireAuthorization("Authenticated");

app.MapPost("/projects/{id}/submit", async (string id, SubmitProjectRequest request, ProjectDbContext db, IntegrationEventPublisher events) =>
{
    var project = await db.Projects.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var nextVersion = await db.Submissions
        .Where(item => item.ProjectId == id)
        .Select(item => (int?)item.Version)
        .MaxAsync() ?? 0;

    var submission = new SubmissionItem
    {
        Id = ShortId.New("SUB"),
        ProjectId = id,
        FileName = request.FileName,
        FileUrl = request.FileUrl,
        Version = nextVersion + 1,
        SubmittedAt = DateTime.UtcNow,
        SubmittedBy = request.SubmittedBy
    };

    project.Status = "Submitted";
    project.UpdatedAt = DateTime.UtcNow;
    db.Submissions.Add(submission);
    await db.SaveChangesAsync();
    await events.PublishAsync("project.submitted", new
    {
        ProjectId = project.Id,
        project.Title,
        project.TeamId,
        project.LecturerId,
        submission.SubmittedBy,
        submission.FileName,
        submission.FileUrl,
        submission.Version,
        submission.SubmittedAt
    });

    return Results.Accepted($"/projects/{id}/history", new
    {
        submission,
        @event = "project.submitted"
    });
}).RequireAuthorization("Submitters");

app.MapPost("/projects/{id}/submissions/upload", async (string id, IFormFile file, ProjectDbContext db, IntegrationEventPublisher events, HttpContext httpContext) =>
{
    var project = await db.Projects.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (project is null)
    {
        return Results.NotFound();
    }

    if (file.Length == 0)
    {
        return Results.BadRequest(new { message = "Uploaded file is empty." });
    }

    var uploadsRoot = Path.Combine(AppContext.BaseDirectory, "uploads");
    Directory.CreateDirectory(uploadsRoot);

    var safeOriginalName = Path.GetFileName(file.FileName);
    var storedName = $"{ShortId.New("FILE")}-{safeOriginalName}";
    var storedPath = Path.Combine(uploadsRoot, storedName);

    await using (var stream = File.Create(storedPath))
    {
        await file.CopyToAsync(stream);
    }

    var nextVersion = await db.Submissions
        .Where(item => item.ProjectId == id)
        .Select(item => (int?)item.Version)
        .MaxAsync() ?? 0;
    var submittedBy = httpContext.User.FindFirst("sub")?.Value
        ?? httpContext.User.Identity?.Name
        ?? "unknown";

    var submission = new SubmissionItem
    {
        Id = ShortId.New("SUB"),
        ProjectId = id,
        FileName = safeOriginalName,
        FileUrl = $"/projects/{id}/submissions/files/{storedName}",
        Version = nextVersion + 1,
        SubmittedAt = DateTime.UtcNow,
        SubmittedBy = submittedBy
    };

    project.Status = "Submitted";
    project.UpdatedAt = DateTime.UtcNow;
    db.Submissions.Add(submission);
    await db.SaveChangesAsync();
    await events.PublishAsync("project.submitted", new
    {
        ProjectId = project.Id,
        project.Title,
        project.TeamId,
        project.LecturerId,
        submission.SubmittedBy,
        submission.FileName,
        submission.FileUrl,
        submission.Version,
        submission.SubmittedAt
    });

    return Results.Created($"/projects/{id}/history", submission);
}).RequireAuthorization("Submitters").DisableAntiforgery();

app.MapGet("/projects/{id}/submissions/files/{storedName}", (string storedName) =>
{
    var uploadsRoot = Path.Combine(AppContext.BaseDirectory, "uploads");
    var fullPath = Path.Combine(uploadsRoot, Path.GetFileName(storedName));
    return File.Exists(fullPath)
        ? Results.File(fullPath, "application/octet-stream", Path.GetFileName(storedName))
        : Results.NotFound();
}).RequireAuthorization("Authenticated");

app.MapGet("/projects/{id}/history", async (string id, ProjectDbContext db) =>
{
    var projectSubmissions = await db.Submissions
        .AsNoTracking()
        .Where(item => item.ProjectId == id)
        .OrderByDescending(item => item.Version)
        .ToListAsync();

    return Results.Ok(projectSubmissions);
}).RequireAuthorization("Authenticated");

app.MapPatch("/projects/{id}/status", async (string id, UpdateProjectStatusRequest request, ProjectDbContext db) =>
{
    var project = await db.Projects.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (project is null)
    {
        return Results.NotFound();
    }

    project.Status = request.Status;
    project.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(project);
}).RequireAuthorization("ProjectManagers");

app.Run();

static bool HasRole(ClaimsPrincipal user, params string[] roles)
{
    var roleClaims = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role));
    return roleClaims.Any(claim => roles.Any(role => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
}

sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options) : DbContext(options)
{
    public DbSet<ProjectItem> Projects => Set<ProjectItem>();
    public DbSet<SubmissionItem> Submissions => Set<SubmissionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectItem>().ToTable("Projects").HasKey(project => project.Id);
        modelBuilder.Entity<SubmissionItem>().ToTable("Submissions").HasKey(submission => submission.Id);
    }
}

sealed class ProjectItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string TeamId { get; set; } = "";
    public string? TeamLeaderId { get; set; }
    public string LecturerId { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public string? RoundId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

sealed class SubmissionItem
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string FileUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Version { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = "";
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

record IntegrationEvent(string Name, DateTime OccurredAt, object Payload);

record CreateProjectRequest(string Title, string? Description, string TeamId, string? TeamLeaderId, string LecturerId, string RoundId);
record SubmitProjectRequest(string FileName, string FileUrl, string SubmittedBy);
record UpdateProjectStatusRequest(string Status);
