using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
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
builder.Services.AddDbContext<IdentityLookupDbContext>(options =>
    options.UseSqlServer(BuildIdentityConnectionString(builder.Configuration)));
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

app.MapGet("/projects", async (string? status, string? round, string? reviewer, ProjectDbContext db, HttpContext httpContext) =>
{
    var query = db.Projects.AsNoTracking();
    var user = httpContext.User;
    var currentUserId = GetCurrentUserId(user);

    if (HasRole(user, "Student"))
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Results.Ok(Array.Empty<ProjectItem>());
        }

        var memberProjectIds = await db.ProjectMembers
            .AsNoTracking()
            .Where(member => member.StudentId == currentUserId)
            .Select(member => member.ProjectId)
            .ToListAsync();
        query = query.Where(project => project.TeamLeaderId == currentUserId || memberProjectIds.Contains(project.Id));
    }

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

app.MapPost("/projects", async (CreateProjectRequest request, ProjectDbContext db, IdentityLookupDbContext identityDb) =>
{
    var validationError = ValidateCreateProjectRequest(request);
    if (validationError is not null)
    {
        return Results.BadRequest(new { message = validationError });
    }

    var normalizedTeamId = request.TeamId.Trim();
    var normalizedLeaderId = NormalizeStudentId(request.TeamLeaderId);
    var memberIds = NormalizeMemberIds(request.MemberStudentIds, normalizedLeaderId);

    var missingStudentError = await ValidateStudentsExistAsync(memberIds, identityDb);
    if (missingStudentError is not null)
    {
        return Results.BadRequest(new { message = missingStudentError });
    }

    var teamExists = await db.Projects.AnyAsync(project => project.TeamId == normalizedTeamId);
    if (teamExists)
    {
        return Results.Conflict(new { message = "Team ID is already assigned to another project." });
    }

    var project = new ProjectItem
    {
        Id = ShortId.New("PRJ"),
        Title = request.Title.Trim(),
        Description = request.Description?.Trim(),
        TeamId = normalizedTeamId,
        TeamLeaderId = normalizedLeaderId,
        LecturerId = request.LecturerId.Trim(),
        Status = "Draft",
        RoundId = string.IsNullOrWhiteSpace(request.RoundId) ? null : request.RoundId.Trim(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    db.Projects.Add(project);
    foreach (var memberId in memberIds)
    {
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            StudentId = memberId,
            IsLeader = memberId == normalizedLeaderId,
            AddedAt = DateTime.UtcNow
        });
    }

    await db.SaveChangesAsync();

    return Results.Created($"/projects/{project.Id}", project);
}).RequireAuthorization("ProjectManagers");

app.MapGet("/projects/{id}", async (string id, ProjectDbContext db, HttpContext httpContext) =>
{
    var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var members = await db.ProjectMembers
        .AsNoTracking()
        .Where(member => member.ProjectId == id)
        .ToListAsync();

    if (!CanAccessProjectWithMembers(httpContext.User, project, members))
    {
        return Results.NotFound();
    }

    return Results.Ok(project);
}).RequireAuthorization("Authenticated");

app.MapPost("/projects/{id}/submit", async (string id, SubmitProjectRequest request, ProjectDbContext db, IntegrationEventPublisher events) =>
{
    if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.FileUrl))
    {
        return Results.BadRequest(new { message = "File name and file URL are required." });
    }

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

    var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".zip" };
    var originalExtension = Path.GetExtension(file.FileName);
    if (!allowedExtensions.Contains(originalExtension, StringComparer.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { message = "Only PDF, Word, or ZIP submissions are allowed." });
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

app.MapGet("/projects/{id}/members", async (string id, ProjectDbContext db, HttpContext httpContext) =>
{
    var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var members = await db.ProjectMembers
        .AsNoTracking()
        .Where(member => member.ProjectId == id)
        .OrderByDescending(member => member.IsLeader)
        .ThenBy(member => member.StudentId)
        .ToListAsync();

    if (!CanAccessProjectWithMembers(httpContext.User, project, members))
    {
        return Results.NotFound();
    }

    return Results.Ok(members);
}).RequireAuthorization("Authenticated");

app.MapPost("/projects/{id}/members", async (string id, AssignProjectMemberRequest request, ProjectDbContext db, IdentityLookupDbContext identityDb, HttpContext httpContext) =>
{
    var studentId = NormalizeStudentId(request.StudentId);
    if (string.IsNullOrWhiteSpace(studentId) || !IsValidStudentId(studentId))
    {
        return Results.BadRequest(new { message = "Student ID must start with 2 letters followed by 6 numbers, for example SE192706." });
    }

    var project = await db.Projects.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (project is null)
    {
        return Results.NotFound();
    }

    if (!CanManageProject(httpContext.User, project))
    {
        return Results.Forbid();
    }

    var studentExists = await StudentExistsAsync(identityDb, studentId);
    if (!studentExists)
    {
        return Results.BadRequest(new { message = $"Student ID {studentId} was not found as an active student." });
    }

    var alreadyAssigned = await db.ProjectMembers.AnyAsync(member => member.ProjectId == id && member.StudentId == studentId);
    if (alreadyAssigned)
    {
        return Results.Conflict(new { message = "This student is already assigned to the project." });
    }

    var member = new ProjectMember
    {
        ProjectId = id,
        StudentId = studentId,
        IsLeader = false,
        AddedAt = DateTime.UtcNow
    };

    db.ProjectMembers.Add(member);
    project.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Created($"/projects/{id}/members", member);
}).RequireAuthorization("ProjectManagers");

app.MapDelete("/projects/{id}/members/{studentId}", async (string id, string studentId, ProjectDbContext db, HttpContext httpContext) =>
{
    var normalizedStudentId = NormalizeStudentId(studentId);
    var project = await db.Projects.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (project is null)
    {
        return Results.NotFound();
    }

    if (!CanManageProject(httpContext.User, project))
    {
        return Results.Forbid();
    }

    var member = await db.ProjectMembers.FirstOrDefaultAsync(candidate =>
        candidate.ProjectId == id && candidate.StudentId == normalizedStudentId);
    if (member is null)
    {
        return Results.NotFound();
    }

    db.ProjectMembers.Remove(member);
    if (project.TeamLeaderId == normalizedStudentId)
    {
        project.TeamLeaderId = null;
    }

    project.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization("ProjectManagers");

app.MapPatch("/projects/{id}/status", async (string id, UpdateProjectStatusRequest request, ProjectDbContext db) =>
{
    if (!IsAllowedProjectStatus(request.Status))
    {
        return Results.BadRequest(new { message = "Status must be Draft, Submitted, In Review, Needs Revision, or Approved." });
    }

    var project = await db.Projects.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (project is null)
    {
        return Results.NotFound();
    }

    project.Status = NormalizeProjectStatus(request.Status);
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

static string? GetCurrentUserId(ClaimsPrincipal user) =>
    user.FindFirst("sub")?.Value
    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? user.FindFirst("nameid")?.Value;

static bool CanAccessProjectWithMembers(ClaimsPrincipal user, ProjectItem project, IEnumerable<ProjectMember> members)
{
    if (!HasRole(user, "Student"))
    {
        return true;
    }

    var currentUserId = GetCurrentUserId(user);
    return !string.IsNullOrWhiteSpace(currentUserId) &&
        (string.Equals(project.TeamLeaderId, currentUserId, StringComparison.OrdinalIgnoreCase) ||
         members.Any(member => string.Equals(member.StudentId, currentUserId, StringComparison.OrdinalIgnoreCase)));
}

static bool CanManageProject(ClaimsPrincipal user, ProjectItem project)
{
    if (HasRole(user, "Admin"))
    {
        return true;
    }

    var currentUserId = GetCurrentUserId(user);
    return HasRole(user, "Lecturer") &&
        !string.IsNullOrWhiteSpace(currentUserId) &&
        string.Equals(project.LecturerId, currentUserId, StringComparison.OrdinalIgnoreCase);
}

static string? ValidateCreateProjectRequest(CreateProjectRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 3)
    {
        return "Project title must be at least 3 characters.";
    }

    if (string.IsNullOrWhiteSpace(request.TeamId))
    {
        return "Team ID is required.";
    }

    if (string.IsNullOrWhiteSpace(request.LecturerId))
    {
        return "Lecturer ID is required.";
    }

    var leaderId = NormalizeStudentId(request.TeamLeaderId);
    if (!string.IsNullOrWhiteSpace(leaderId) && !IsValidStudentId(leaderId))
    {
        return "Team leader ID must start with 2 letters followed by 6 numbers, for example SE192706.";
    }

    foreach (var memberId in request.MemberStudentIds ?? [])
    {
        var normalizedMemberId = NormalizeStudentId(memberId);
        if (string.IsNullOrWhiteSpace(normalizedMemberId) || !IsValidStudentId(normalizedMemberId))
        {
            return "Each member Student ID must start with 2 letters followed by 6 numbers, for example SE192706.";
        }
    }

    return null;
}

static List<string> NormalizeMemberIds(string[]? memberStudentIds, string? leaderId)
{
    var members = (memberStudentIds ?? [])
        .Select(NormalizeStudentId)
        .Where(memberId => !string.IsNullOrWhiteSpace(memberId))
        .Select(memberId => memberId!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (!string.IsNullOrWhiteSpace(leaderId))
    {
        members.Add(leaderId);
    }

    return members.OrderBy(memberId => memberId).ToList();
}

static string? NormalizeStudentId(string? studentId) =>
    string.IsNullOrWhiteSpace(studentId) ? null : studentId.Trim().ToUpperInvariant();

static bool IsValidStudentId(string studentId) =>
    Regex.IsMatch(studentId, "^[A-Z]{2}\\d{6}$");

static bool IsAllowedProjectStatus(string status) => !string.IsNullOrWhiteSpace(NormalizeProjectStatus(status));

static string BuildIdentityConnectionString(IConfiguration configuration)
{
    var explicitConnection = configuration.GetConnectionString("IdentityConnection");
    if (!string.IsNullOrWhiteSpace(explicitConnection))
    {
        return explicitConnection;
    }

    var defaultConnection = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
    return new SqlConnectionStringBuilder(defaultConnection)
    {
        InitialCatalog = "IdentityDb"
    }.ConnectionString;
}

static async Task<string?> ValidateStudentsExistAsync(IEnumerable<string> studentIds, IdentityLookupDbContext identityDb)
{
    var requestedIds = studentIds
        .Where(studentId => !string.IsNullOrWhiteSpace(studentId))
        .Select(studentId => studentId.Trim().ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (requestedIds.Count == 0)
    {
        return null;
    }

    var existingIds = await identityDb.Users
        .AsNoTracking()
        .Where(user => user.IsActive &&
            user.Role == "Student" &&
            ((user.StudentId != null && requestedIds.Contains(user.StudentId)) || requestedIds.Contains(user.Id)))
        .Select(user => user.StudentId ?? user.Id)
        .ToListAsync();

    var existingSet = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var missingId = requestedIds.FirstOrDefault(studentId => !existingSet.Contains(studentId));
    return missingId is null ? null : $"Student ID {missingId} was not found as an active student.";
}

static async Task<bool> StudentExistsAsync(IdentityLookupDbContext identityDb, string studentId) =>
    await ValidateStudentsExistAsync([studentId], identityDb) is null;

static string NormalizeProjectStatus(string status)
{
    string[] allowedStatuses = ["Draft", "Submitted", "In Review", "Needs Revision", "Approved"];
    return allowedStatuses.FirstOrDefault(allowedStatus =>
        string.Equals(allowedStatus, status?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
}

sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options) : DbContext(options)
{
    public DbSet<ProjectItem> Projects => Set<ProjectItem>();
    public DbSet<SubmissionItem> Submissions => Set<SubmissionItem>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectItem>().ToTable("Projects").HasKey(project => project.Id);
        modelBuilder.Entity<ProjectItem>().HasIndex(project => project.TeamId).IsUnique();
        modelBuilder.Entity<SubmissionItem>().ToTable("Submissions").HasKey(submission => submission.Id);
        modelBuilder.Entity<ProjectMember>().ToTable("ProjectMembers").HasKey(member => new { member.ProjectId, member.StudentId });
        modelBuilder.Entity<ProjectMember>()
            .HasOne(member => member.Project)
            .WithMany()
            .HasForeignKey(member => member.ProjectId);
    }
}

sealed class IdentityLookupDbContext(DbContextOptions<IdentityLookupDbContext> options) : DbContext(options)
{
    public DbSet<IdentityUserLookup> Users => Set<IdentityUserLookup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserLookup>().ToTable("Users").HasKey(user => user.Id);
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

sealed class ProjectMember
{
    public string ProjectId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public bool IsLeader { get; set; }
    public DateTime AddedAt { get; set; }
    public ProjectItem? Project { get; set; }
}

sealed class IdentityUserLookup
{
    public string Id { get; set; } = "";
    public string? StudentId { get; set; }
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
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

record CreateProjectRequest(string Title, string? Description, string TeamId, string? TeamLeaderId, string LecturerId, string? RoundId, string[]? MemberStudentIds);
record SubmitProjectRequest(string FileName, string FileUrl, string SubmittedBy);
record UpdateProjectStatusRequest(string Status);
record AssignProjectMemberRequest(string StudentId);
