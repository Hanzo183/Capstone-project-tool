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
builder.Services.AddDbContext<ProjectDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

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
});

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
});

app.MapGet("/projects/{id}", async (string id, ProjectDbContext db) =>
{
    var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    return project is null ? Results.NotFound() : Results.Ok(project);
});

app.MapPost("/projects/{id}/submit", async (string id, SubmitProjectRequest request, ProjectDbContext db) =>
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

    return Results.Accepted($"/projects/{id}/history", new
    {
        submission,
        @event = "project.submitted"
    });
});

app.MapGet("/projects/{id}/history", async (string id, ProjectDbContext db) =>
{
    var projectSubmissions = await db.Submissions
        .AsNoTracking()
        .Where(item => item.ProjectId == id)
        .OrderByDescending(item => item.Version)
        .ToListAsync();

    return Results.Ok(projectSubmissions);
});

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
});

app.Run();

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

record CreateProjectRequest(string Title, string? Description, string TeamId, string? TeamLeaderId, string LecturerId, string RoundId);
record SubmitProjectRequest(string FileName, string FileUrl, string SubmittedBy);
record UpdateProjectStatusRequest(string Status);
