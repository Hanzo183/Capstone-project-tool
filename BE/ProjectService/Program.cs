using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Confluent.Kafka;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UserProfile;

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
    options.AddPolicy("ProjectManagers", policy => policy.RequireAssertion(context => AuthHelpers.HasRole(context.User, "Admin", "Lecturer")));
    options.AddPolicy("Submitters", policy => policy.RequireAssertion(context => AuthHelpers.HasRole(context.User, "Admin", "Student")));
});
builder.Services.AddDbContext<ProjectDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IProjectRepository, EfProjectRepository>();
builder.Services.AddScoped<ProjectManagementService>();
builder.Services.AddSingleton<IntegrationEventPublisher>();
builder.Services.AddSingleton(sp =>
{
    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    var address = sp.GetRequiredService<IConfiguration>()["Grpc:UserProfileAddress"] ?? "http://localhost:8086";
    return new UserProfileLookup.UserProfileLookupClient(GrpcChannel.ForAddress(address));
});
builder.Services.AddScoped<UserProfileGateway>();

var app = builder.Build();
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
        logger.LogError(feature?.Error, "Unhandled ProjectService exception.");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = "An unexpected project service error occurred." });
    });
});
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

app.MapGet("/projects", async (
    string? search,
    string? status,
    string? round,
    string? reviewer,
    string? sortBy,
    string? sortDir,
    int? page,
    int? pageSize,
    ProjectManagementService projects,
    HttpContext httpContext) =>
{
    var query = new ProjectSearchRequest(search, status, round, reviewer, sortBy, sortDir, page, pageSize);
    return Results.Ok(await projects.SearchAsync(query, httpContext.User));
}).RequireAuthorization("Authenticated");

app.MapPost("/projects", async (CreateProjectRequest request, ProjectManagementService projects) =>
    ToHttpResult(await projects.CreateAsync(request), createdLocation: value => $"/projects/{value.Id}"))
    .RequireAuthorization("ProjectManagers");

app.MapGet("/projects/{id}", async (string id, ProjectManagementService projects, HttpContext httpContext) =>
    ToHttpResult(await projects.GetAsync(id, httpContext.User)))
    .RequireAuthorization("Authenticated");

app.MapPut("/projects/{id}", async (string id, UpdateProjectRequest request, ProjectManagementService projects, HttpContext httpContext) =>
    ToHttpResult(await projects.UpdateAsync(id, request, httpContext.User)))
    .RequireAuthorization("ProjectManagers");

app.MapDelete("/projects/{id}", async (string id, ProjectManagementService projects, HttpContext httpContext) =>
    ToHttpResult(await projects.DeleteAsync(id, httpContext.User)))
    .RequireAuthorization("ProjectManagers");

app.MapPost("/projects/{id}/submit", async (string id, SubmitProjectRequest request, ProjectManagementService projects) =>
    ToHttpResult(await projects.SubmitAsync(id, request), createdLocation: _ => $"/projects/{id}/history"))
    .RequireAuthorization("Submitters");

app.MapPost("/projects/{id}/submissions/upload", async (string id, IFormFile file, ProjectManagementService projects, HttpContext httpContext) =>
    ToHttpResult(await projects.UploadSubmissionAsync(id, file, httpContext.User), createdLocation: _ => $"/projects/{id}/history"))
    .RequireAuthorization("Submitters")
    .DisableAntiforgery();

app.MapGet("/projects/{id}/submissions/files/{storedName}", (string storedName) =>
{
    var uploadsRoot = Path.Combine(AppContext.BaseDirectory, "uploads");
    var fullPath = Path.Combine(uploadsRoot, Path.GetFileName(storedName));
    return File.Exists(fullPath)
        ? Results.File(fullPath, "application/octet-stream", Path.GetFileName(storedName))
        : Results.NotFound();
}).RequireAuthorization("Authenticated");

app.MapGet("/projects/{id}/history", async (string id, ProjectManagementService projects, int? page, int? pageSize) =>
    Results.Ok(await projects.GetSubmissionHistoryAsync(id, page, pageSize)))
    .RequireAuthorization("Authenticated");

app.MapGet("/projects/{id}/members", async (string id, ProjectManagementService projects, HttpContext httpContext) =>
    ToHttpResult(await projects.GetMembersAsync(id, httpContext.User)))
    .RequireAuthorization("Authenticated");

app.MapPost("/projects/{id}/members", async (string id, AssignProjectMemberRequest request, ProjectManagementService projects, HttpContext httpContext) =>
    ToHttpResult(await projects.AddMemberAsync(id, request, httpContext.User), createdLocation: _ => $"/projects/{id}/members"))
    .RequireAuthorization("ProjectManagers");

app.MapDelete("/projects/{id}/members/{studentId}", async (string id, string studentId, ProjectManagementService projects, HttpContext httpContext) =>
    ToHttpResult(await projects.RemoveMemberAsync(id, studentId, httpContext.User)))
    .RequireAuthorization("ProjectManagers");

app.MapPatch("/projects/{id}/status", async (string id, UpdateProjectStatusRequest request, ProjectManagementService projects, HttpContext httpContext) =>
    ToHttpResult(await projects.UpdateStatusAsync(id, request, httpContext.User)))
    .RequireAuthorization("ProjectManagers");

app.Run();

static IResult ToHttpResult<T>(ServiceResult<T> result, Func<T, string>? createdLocation = null)
{
    return result.StatusCode switch
    {
        StatusCodes.Status200OK => Results.Ok(result.Value),
        StatusCodes.Status201Created => Results.Created(createdLocation?.Invoke(result.Value!) ?? "", result.Value),
        StatusCodes.Status202Accepted => Results.Accepted(createdLocation?.Invoke(result.Value!) ?? "", result.Value),
        StatusCodes.Status204NoContent => Results.NoContent(),
        StatusCodes.Status400BadRequest => Results.BadRequest(new { message = result.Message }),
        StatusCodes.Status403Forbidden => Results.Forbid(),
        StatusCodes.Status404NotFound => Results.NotFound(new { message = result.Message }),
        StatusCodes.Status409Conflict => Results.Conflict(new { message = result.Message }),
        _ => Results.Json(new { message = result.Message }, statusCode: result.StatusCode)
    };
}

sealed class ProjectManagementService(
    IProjectRepository repository,
    UserProfileGateway userProfiles,
    IntegrationEventPublisher events)
{
    public Task<PagedResult<ProjectItem>> SearchAsync(ProjectSearchRequest request, ClaimsPrincipal user)
    {
        var criteria = ProjectSearchCriteria.From(request, AuthHelpers.GetCurrentUserId(user), AuthHelpers.HasRole(user, "Student"));
        return repository.SearchAsync(criteria);
    }

    public async Task<ServiceResult<ProjectItem>> CreateAsync(CreateProjectRequest request)
    {
        var validationError = ProjectValidation.ValidateCreate(request);
        if (validationError is not null)
        {
            return ServiceResult<ProjectItem>.BadRequest(validationError);
        }

        var normalizedTeamId = request.TeamId.Trim();
        var normalizedLeaderId = ProjectValidation.NormalizeStudentId(request.TeamLeaderId);
        var memberIds = ProjectValidation.NormalizeMemberIds(request.MemberStudentIds, normalizedLeaderId);

        var missingStudentError = await userProfiles.ValidateStudentsExistAsync(memberIds);
        if (missingStudentError is not null)
        {
            return ServiceResult<ProjectItem>.BadRequest(missingStudentError);
        }

        if (await repository.TeamExistsAsync(normalizedTeamId))
        {
            return ServiceResult<ProjectItem>.Conflict("Team ID is already assigned to another project.");
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

        await repository.AddAsync(project, memberIds.Select(memberId => new ProjectMember
        {
            ProjectId = project.Id,
            StudentId = memberId,
            IsLeader = memberId == normalizedLeaderId,
            AddedAt = DateTime.UtcNow
        }));

        return ServiceResult<ProjectItem>.Created(project);
    }

    public async Task<ServiceResult<ProjectItem>> GetAsync(string id, ClaimsPrincipal user)
    {
        var project = await repository.GetAsync(id, tracking: false);
        if (project is null)
        {
            return ServiceResult<ProjectItem>.NotFound("Project was not found.");
        }

        var members = await repository.GetMembersAsync(id);
        return CanAccess(user, project, members)
            ? ServiceResult<ProjectItem>.Ok(project)
            : ServiceResult<ProjectItem>.NotFound("Project was not found.");
    }

    public async Task<ServiceResult<ProjectItem>> UpdateAsync(string id, UpdateProjectRequest request, ClaimsPrincipal user)
    {
        var validationError = ProjectValidation.ValidateUpdate(request);
        if (validationError is not null)
        {
            return ServiceResult<ProjectItem>.BadRequest(validationError);
        }

        var project = await repository.GetAsync(id, tracking: true);
        if (project is null)
        {
            return ServiceResult<ProjectItem>.NotFound("Project was not found.");
        }

        if (!CanManage(user, project))
        {
            return ServiceResult<ProjectItem>.Forbidden();
        }

        var normalizedTeamId = request.TeamId.Trim();
        if (!string.Equals(project.TeamId, normalizedTeamId, StringComparison.OrdinalIgnoreCase) &&
            await repository.TeamExistsAsync(normalizedTeamId, id))
        {
            return ServiceResult<ProjectItem>.Conflict("Team ID is already assigned to another project.");
        }

        var normalizedLeaderId = ProjectValidation.NormalizeStudentId(request.TeamLeaderId);
        var memberIds = ProjectValidation.NormalizeMemberIds(request.MemberStudentIds, normalizedLeaderId);
        var missingStudentError = await userProfiles.ValidateStudentsExistAsync(memberIds);
        if (missingStudentError is not null)
        {
            return ServiceResult<ProjectItem>.BadRequest(missingStudentError);
        }

        project.Title = request.Title.Trim();
        project.Description = request.Description?.Trim();
        project.TeamId = normalizedTeamId;
        project.TeamLeaderId = normalizedLeaderId;
        project.LecturerId = request.LecturerId.Trim();
        project.RoundId = string.IsNullOrWhiteSpace(request.RoundId) ? null : request.RoundId.Trim();
        project.UpdatedAt = DateTime.UtcNow;
        await repository.ReplaceMembersAsync(project.Id, memberIds, normalizedLeaderId);
        await repository.SaveChangesAsync();

        return ServiceResult<ProjectItem>.Ok(project);
    }

    public async Task<ServiceResult<object>> DeleteAsync(string id, ClaimsPrincipal user)
    {
        var project = await repository.GetAsync(id, tracking: true);
        if (project is null)
        {
            return ServiceResult<object>.NotFound("Project was not found.");
        }

        if (!CanManage(user, project))
        {
            return ServiceResult<object>.Forbidden();
        }

        await repository.DeleteAsync(project);
        return ServiceResult<object>.NoContent();
    }

    public async Task<ServiceResult<object>> SubmitAsync(string id, SubmitProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.FileUrl))
        {
            return ServiceResult<object>.BadRequest("File name and file URL are required.");
        }

        var project = await repository.GetAsync(id, tracking: true);
        if (project is null)
        {
            return ServiceResult<object>.NotFound("Project was not found.");
        }

        var submission = await AddSubmissionAsync(project, request.FileName, request.FileUrl, request.SubmittedBy);
        return ServiceResult<object>.Accepted(new { submission, @event = "project.submitted" });
    }

    public async Task<ServiceResult<SubmissionItem>> UploadSubmissionAsync(string id, IFormFile file, ClaimsPrincipal user)
    {
        var project = await repository.GetAsync(id, tracking: true);
        if (project is null)
        {
            return ServiceResult<SubmissionItem>.NotFound("Project was not found.");
        }

        if (file.Length == 0)
        {
            return ServiceResult<SubmissionItem>.BadRequest("Uploaded file is empty.");
        }

        var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".zip" };
        var originalExtension = Path.GetExtension(file.FileName);
        if (!allowedExtensions.Contains(originalExtension, StringComparer.OrdinalIgnoreCase))
        {
            return ServiceResult<SubmissionItem>.BadRequest("Only PDF, Word, or ZIP submissions are allowed.");
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

        var submittedBy = AuthHelpers.GetCurrentUserId(user) ?? user.Identity?.Name ?? "unknown";
        var submission = await AddSubmissionAsync(project, safeOriginalName, $"/projects/{id}/submissions/files/{storedName}", submittedBy);
        return ServiceResult<SubmissionItem>.Created(submission);
    }

    public Task<PagedResult<SubmissionItem>> GetSubmissionHistoryAsync(string id, int? page, int? pageSize) =>
        repository.GetSubmissionHistoryAsync(id, Paging.Normalize(page, pageSize));

    public async Task<ServiceResult<IReadOnlyList<ProjectMember>>> GetMembersAsync(string id, ClaimsPrincipal user)
    {
        var project = await repository.GetAsync(id, tracking: false);
        if (project is null)
        {
            return ServiceResult<IReadOnlyList<ProjectMember>>.NotFound("Project was not found.");
        }

        var members = await repository.GetMembersAsync(id);
        return CanAccess(user, project, members)
            ? ServiceResult<IReadOnlyList<ProjectMember>>.Ok(members)
            : ServiceResult<IReadOnlyList<ProjectMember>>.NotFound("Project was not found.");
    }

    public async Task<ServiceResult<ProjectMember>> AddMemberAsync(string id, AssignProjectMemberRequest request, ClaimsPrincipal user)
    {
        var studentId = ProjectValidation.NormalizeStudentId(request.StudentId);
        if (string.IsNullOrWhiteSpace(studentId) || !ProjectValidation.IsValidStudentId(studentId))
        {
            return ServiceResult<ProjectMember>.BadRequest("Student ID must start with 2 letters followed by 6 numbers, for example SE192706.");
        }

        var project = await repository.GetAsync(id, tracking: true);
        if (project is null)
        {
            return ServiceResult<ProjectMember>.NotFound("Project was not found.");
        }

        if (!CanManage(user, project))
        {
            return ServiceResult<ProjectMember>.Forbidden();
        }

        var missingStudentError = await userProfiles.ValidateStudentsExistAsync([studentId]);
        if (missingStudentError is not null)
        {
            return ServiceResult<ProjectMember>.BadRequest(missingStudentError);
        }

        if (await repository.MemberExistsAsync(id, studentId))
        {
            return ServiceResult<ProjectMember>.Conflict("This student is already assigned to the project.");
        }

        var member = new ProjectMember
        {
            ProjectId = id,
            StudentId = studentId,
            IsLeader = false,
            AddedAt = DateTime.UtcNow
        };
        await repository.AddMemberAsync(project, member);

        return ServiceResult<ProjectMember>.Created(member);
    }

    public async Task<ServiceResult<object>> RemoveMemberAsync(string id, string studentId, ClaimsPrincipal user)
    {
        var normalizedStudentId = ProjectValidation.NormalizeStudentId(studentId);
        var project = await repository.GetAsync(id, tracking: true);
        if (project is null)
        {
            return ServiceResult<object>.NotFound("Project was not found.");
        }

        if (!CanManage(user, project))
        {
            return ServiceResult<object>.Forbidden();
        }

        var removed = await repository.RemoveMemberAsync(project, normalizedStudentId);
        return removed ? ServiceResult<object>.NoContent() : ServiceResult<object>.NotFound("Project member was not found.");
    }

    public async Task<ServiceResult<ProjectItem>> UpdateStatusAsync(string id, UpdateProjectStatusRequest request, ClaimsPrincipal user)
    {
        if (!ProjectValidation.IsAllowedProjectStatus(request.Status))
        {
            return ServiceResult<ProjectItem>.BadRequest("Status must be Draft, Submitted, In Review, Needs Revision, or Approved.");
        }

        var project = await repository.GetAsync(id, tracking: true);
        if (project is null)
        {
            return ServiceResult<ProjectItem>.NotFound("Project was not found.");
        }

        if (!CanManage(user, project))
        {
            return ServiceResult<ProjectItem>.Forbidden();
        }

        project.Status = ProjectValidation.NormalizeProjectStatus(request.Status);
        project.UpdatedAt = DateTime.UtcNow;
        await repository.SaveChangesAsync();
        return ServiceResult<ProjectItem>.Ok(project);
    }

    private async Task<SubmissionItem> AddSubmissionAsync(ProjectItem project, string fileName, string fileUrl, string submittedBy)
    {
        var submission = new SubmissionItem
        {
            Id = ShortId.New("SUB"),
            ProjectId = project.Id,
            FileName = fileName,
            FileUrl = fileUrl,
            Version = await repository.GetNextSubmissionVersionAsync(project.Id),
            SubmittedAt = DateTime.UtcNow,
            SubmittedBy = submittedBy
        };

        project.Status = "Submitted";
        project.UpdatedAt = DateTime.UtcNow;
        await repository.AddSubmissionAsync(submission);
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

        return submission;
    }

    private static bool CanAccess(ClaimsPrincipal user, ProjectItem project, IEnumerable<ProjectMember> members)
    {
        if (!AuthHelpers.HasRole(user, "Student"))
        {
            return true;
        }

        var currentUserId = AuthHelpers.GetCurrentUserId(user);
        return !string.IsNullOrWhiteSpace(currentUserId) &&
            (string.Equals(project.TeamLeaderId, currentUserId, StringComparison.OrdinalIgnoreCase) ||
             members.Any(member => string.Equals(member.StudentId, currentUserId, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool CanManage(ClaimsPrincipal user, ProjectItem project)
    {
        if (AuthHelpers.HasRole(user, "Admin"))
        {
            return true;
        }

        var currentUserId = AuthHelpers.GetCurrentUserId(user);
        return AuthHelpers.HasRole(user, "Lecturer") &&
            !string.IsNullOrWhiteSpace(currentUserId) &&
            string.Equals(project.LecturerId, currentUserId, StringComparison.OrdinalIgnoreCase);
    }
}

interface IProjectRepository
{
    Task<PagedResult<ProjectItem>> SearchAsync(ProjectSearchCriteria criteria);
    Task<ProjectItem?> GetAsync(string id, bool tracking);
    Task<IReadOnlyList<ProjectMember>> GetMembersAsync(string projectId);
    Task<bool> TeamExistsAsync(string teamId, string? exceptProjectId = null);
    Task AddAsync(ProjectItem project, IEnumerable<ProjectMember> members);
    Task DeleteAsync(ProjectItem project);
    Task ReplaceMembersAsync(string projectId, IReadOnlyList<string> studentIds, string? leaderId);
    Task<bool> MemberExistsAsync(string projectId, string studentId);
    Task AddMemberAsync(ProjectItem project, ProjectMember member);
    Task<bool> RemoveMemberAsync(ProjectItem project, string? studentId);
    Task<int> GetNextSubmissionVersionAsync(string projectId);
    Task AddSubmissionAsync(SubmissionItem submission);
    Task<PagedResult<SubmissionItem>> GetSubmissionHistoryAsync(string projectId, Paging paging);
    Task SaveChangesAsync();
}

sealed class EfProjectRepository(ProjectDbContext db) : IProjectRepository
{
    public async Task<PagedResult<ProjectItem>> SearchAsync(ProjectSearchCriteria criteria)
    {
        var query = db.Projects.AsNoTracking();

        if (criteria.RestrictToStudent)
        {
            if (string.IsNullOrWhiteSpace(criteria.StudentId))
            {
                return PagedResult<ProjectItem>.Empty(criteria.Page, criteria.PageSize);
            }

            var memberProjectIds = await db.ProjectMembers
                .AsNoTracking()
                .Where(member => member.StudentId == criteria.StudentId)
                .Select(member => member.ProjectId)
                .ToListAsync();
            query = query.Where(project => project.TeamLeaderId == criteria.StudentId || memberProjectIds.Contains(project.Id));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            query = query.Where(project =>
                project.Title.Contains(criteria.Search) ||
                project.TeamId.Contains(criteria.Search) ||
                (project.Description != null && project.Description.Contains(criteria.Search)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            query = query.Where(project => project.Status == criteria.Status);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Round))
        {
            query = query.Where(project => project.RoundId != null && project.RoundId.Contains(criteria.Round));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Reviewer))
        {
            query = query.Where(project => project.LecturerId == criteria.Reviewer);
        }

        var totalCount = await query.CountAsync();
        query = ApplySort(query, criteria.SortBy, criteria.SortDescending);

        var items = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync();

        return new PagedResult<ProjectItem>(items, criteria.Page, criteria.PageSize, totalCount);
    }

    public Task<ProjectItem?> GetAsync(string id, bool tracking)
    {
        var query = tracking ? db.Projects : db.Projects.AsNoTracking();
        return query.FirstOrDefaultAsync(candidate => candidate.Id == id);
    }

    public async Task<IReadOnlyList<ProjectMember>> GetMembersAsync(string projectId) =>
        await db.ProjectMembers
            .AsNoTracking()
            .Where(member => member.ProjectId == projectId)
            .OrderByDescending(member => member.IsLeader)
            .ThenBy(member => member.StudentId)
            .ToListAsync();

    public Task<bool> TeamExistsAsync(string teamId, string? exceptProjectId = null) =>
        db.Projects.AnyAsync(project => project.TeamId == teamId && (exceptProjectId == null || project.Id != exceptProjectId));

    public async Task AddAsync(ProjectItem project, IEnumerable<ProjectMember> members)
    {
        db.Projects.Add(project);
        db.ProjectMembers.AddRange(members);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(ProjectItem project)
    {
        var submissions = await db.Submissions.Where(submission => submission.ProjectId == project.Id).ToListAsync();
        var members = await db.ProjectMembers.Where(member => member.ProjectId == project.Id).ToListAsync();
        db.Submissions.RemoveRange(submissions);
        db.ProjectMembers.RemoveRange(members);
        db.Projects.Remove(project);
        await db.SaveChangesAsync();
    }

    public async Task ReplaceMembersAsync(string projectId, IReadOnlyList<string> studentIds, string? leaderId)
    {
        var existing = await db.ProjectMembers.Where(member => member.ProjectId == projectId).ToListAsync();
        db.ProjectMembers.RemoveRange(existing);
        db.ProjectMembers.AddRange(studentIds.Select(studentId => new ProjectMember
        {
            ProjectId = projectId,
            StudentId = studentId,
            IsLeader = studentId == leaderId,
            AddedAt = DateTime.UtcNow
        }));
    }

    public Task<bool> MemberExistsAsync(string projectId, string studentId) =>
        db.ProjectMembers.AnyAsync(member => member.ProjectId == projectId && member.StudentId == studentId);

    public async Task AddMemberAsync(ProjectItem project, ProjectMember member)
    {
        db.ProjectMembers.Add(member);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<bool> RemoveMemberAsync(ProjectItem project, string? studentId)
    {
        var member = await db.ProjectMembers.FirstOrDefaultAsync(candidate =>
            candidate.ProjectId == project.Id && candidate.StudentId == studentId);
        if (member is null)
        {
            return false;
        }

        db.ProjectMembers.Remove(member);
        if (project.TeamLeaderId == studentId)
        {
            project.TeamLeaderId = null;
        }

        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<int> GetNextSubmissionVersionAsync(string projectId)
    {
        var currentVersion = await db.Submissions
            .Where(item => item.ProjectId == projectId)
            .Select(item => (int?)item.Version)
            .MaxAsync() ?? 0;
        return currentVersion + 1;
    }

    public async Task AddSubmissionAsync(SubmissionItem submission)
    {
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();
    }

    public async Task<PagedResult<SubmissionItem>> GetSubmissionHistoryAsync(string projectId, Paging paging)
    {
        var query = db.Submissions
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.Version);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync();
        return new PagedResult<SubmissionItem>(items, paging.Page, paging.PageSize, totalCount);
    }

    public Task SaveChangesAsync() => db.SaveChangesAsync();

    private static IQueryable<ProjectItem> ApplySort(IQueryable<ProjectItem> query, string sortBy, bool descending)
    {
        return (sortBy.ToLowerInvariant(), descending) switch
        {
            ("title", false) => query.OrderBy(project => project.Title),
            ("title", true) => query.OrderByDescending(project => project.Title),
            ("status", false) => query.OrderBy(project => project.Status),
            ("status", true) => query.OrderByDescending(project => project.Status),
            ("team", false) => query.OrderBy(project => project.TeamId),
            ("team", true) => query.OrderByDescending(project => project.TeamId),
            ("created", false) => query.OrderBy(project => project.CreatedAt),
            ("created", true) => query.OrderByDescending(project => project.CreatedAt),
            ("updated", false) => query.OrderBy(project => project.UpdatedAt),
            _ => query.OrderByDescending(project => project.UpdatedAt)
        };
    }
}

sealed class UserProfileGateway(UserProfileLookup.UserProfileLookupClient client, ILogger<UserProfileGateway> logger)
{
    public async Task<string?> ValidateStudentsExistAsync(IEnumerable<string> studentIds)
    {
        foreach (var studentId in studentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var reply = await client.ValidateStudentAsync(new StudentValidationRequest { StudentId = studentId });
                if (!reply.IsValid)
                {
                    return reply.Message;
                }
            }
            catch (RpcException ex)
            {
                logger.LogWarning(ex, "gRPC user profile validation failed for {StudentId}.", studentId);
                return "User profile gRPC service is unavailable. Please try again later.";
            }
        }

        return null;
    }
}

static class AuthHelpers
{
    public static bool HasRole(ClaimsPrincipal user, params string[] roles)
    {
        var roleClaims = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role));
        return roleClaims.Any(claim => roles.Any(role => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
    }

    public static string? GetCurrentUserId(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value
        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("nameid")?.Value;
}

static class ProjectValidation
{
    public static string? ValidateCreate(CreateProjectRequest request) =>
        ValidateRequiredProjectFields(request.Title, request.TeamId, request.TeamLeaderId, request.LecturerId, request.MemberStudentIds);

    public static string? ValidateUpdate(UpdateProjectRequest request) =>
        ValidateRequiredProjectFields(request.Title, request.TeamId, request.TeamLeaderId, request.LecturerId, request.MemberStudentIds);

    private static string? ValidateRequiredProjectFields(string title, string teamId, string? teamLeaderId, string lecturerId, string[]? memberStudentIds)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < 3)
        {
            return "Project title must be at least 3 characters.";
        }

        if (string.IsNullOrWhiteSpace(teamId))
        {
            return "Team ID is required.";
        }

        if (string.IsNullOrWhiteSpace(lecturerId))
        {
            return "Lecturer ID is required.";
        }

        var leaderId = NormalizeStudentId(teamLeaderId);
        if (!string.IsNullOrWhiteSpace(leaderId) && !IsValidStudentId(leaderId))
        {
            return "Team leader ID must start with 2 letters followed by 6 numbers, for example SE192706.";
        }

        foreach (var memberId in memberStudentIds ?? [])
        {
            var normalizedMemberId = NormalizeStudentId(memberId);
            if (string.IsNullOrWhiteSpace(normalizedMemberId) || !IsValidStudentId(normalizedMemberId))
            {
                return "Each member Student ID must start with 2 letters followed by 6 numbers, for example SE192706.";
            }
        }

        return null;
    }

    public static List<string> NormalizeMemberIds(string[]? memberStudentIds, string? leaderId)
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

    public static string? NormalizeStudentId(string? studentId) =>
        string.IsNullOrWhiteSpace(studentId) ? null : studentId.Trim().ToUpperInvariant();

    public static bool IsValidStudentId(string studentId) =>
        Regex.IsMatch(studentId, "^[A-Z]{2}\\d{6}$");

    public static bool IsAllowedProjectStatus(string status) => !string.IsNullOrWhiteSpace(NormalizeProjectStatus(status));

    public static string NormalizeProjectStatus(string status)
    {
        string[] allowedStatuses = ["Draft", "Submitted", "In Review", "Needs Revision", "Approved"];
        return allowedStatuses.FirstOrDefault(allowedStatus =>
            string.Equals(allowedStatus, status?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
    }
}

sealed class IntegrationEventPublisher(IConfiguration configuration, ILogger<IntegrationEventPublisher> logger)
{
    public async Task PublishAsync(string eventName, object payload)
    {
        try
        {
            var config = new ProducerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
                Acks = Acks.All
            };
            using var producer = new ProducerBuilder<string, string>(config).Build();
            var envelope = JsonSerializer.Serialize(new IntegrationEvent(eventName, DateTime.UtcNow, payload));
            await producer.ProduceAsync(eventName, new Message<string, string>
            {
                Key = eventName,
                Value = envelope
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not publish Kafka integration event {EventName}.", eventName);
        }
    }
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
        modelBuilder.Entity<SubmissionItem>()
            .HasOne(submission => submission.Project)
            .WithMany()
            .HasForeignKey(submission => submission.ProjectId);
        modelBuilder.Entity<ProjectMember>().ToTable("ProjectMembers").HasKey(member => new { member.ProjectId, member.StudentId });
        modelBuilder.Entity<ProjectMember>()
            .HasOne(member => member.Project)
            .WithMany()
            .HasForeignKey(member => member.ProjectId);
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
    public ProjectItem? Project { get; set; }
}

sealed class ProjectMember
{
    public string ProjectId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public bool IsLeader { get; set; }
    public DateTime AddedAt { get; set; }
    public ProjectItem? Project { get; set; }
}

static class ShortId
{
    public static string New(string prefix) => $"{prefix}-{RandomNumberGenerator.GetHexString(8)}";
}

record IntegrationEvent(string Name, DateTime OccurredAt, object Payload);
record ProjectSearchRequest(string? Search, string? Status, string? Round, string? Reviewer, string? SortBy, string? SortDir, int? Page, int? PageSize);
record CreateProjectRequest(string Title, string? Description, string TeamId, string? TeamLeaderId, string LecturerId, string? RoundId, string[]? MemberStudentIds);
record UpdateProjectRequest(string Title, string? Description, string TeamId, string? TeamLeaderId, string LecturerId, string? RoundId, string[]? MemberStudentIds);
record SubmitProjectRequest(string FileName, string FileUrl, string SubmittedBy);
record UpdateProjectStatusRequest(string Status);
record AssignProjectMemberRequest(string StudentId);
record ProjectSearchCriteria(string? Search, string? Status, string? Round, string? Reviewer, string SortBy, bool SortDescending, int Page, int PageSize, string? StudentId, bool RestrictToStudent)
{
    public static ProjectSearchCriteria From(ProjectSearchRequest request, string? studentId, bool restrictToStudent)
    {
        var paging = Paging.Normalize(request.Page, request.PageSize);
        return new ProjectSearchCriteria(
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim(),
            string.IsNullOrWhiteSpace(request.Round) ? null : request.Round.Trim(),
            string.IsNullOrWhiteSpace(request.Reviewer) ? null : request.Reviewer.Trim(),
            string.IsNullOrWhiteSpace(request.SortBy) ? "updated" : request.SortBy.Trim(),
            string.Equals(request.SortDir, "asc", StringComparison.OrdinalIgnoreCase) ? false : true,
            paging.Page,
            paging.PageSize,
            studentId,
            restrictToStudent);
    }
}

record Paging(int Page, int PageSize)
{
    public static Paging Normalize(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, 100));
}

record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

sealed record ServiceResult<T>(int StatusCode, T? Value = default, string? Message = null)
{
    public static ServiceResult<T> Ok(T value) => new(StatusCodes.Status200OK, value);
    public static ServiceResult<T> Created(T value) => new(StatusCodes.Status201Created, value);
    public static ServiceResult<T> Accepted(T value) => new(StatusCodes.Status202Accepted, value);
    public static ServiceResult<T> NoContent() => new(StatusCodes.Status204NoContent);
    public static ServiceResult<T> BadRequest(string message) => new(StatusCodes.Status400BadRequest, default, message);
    public static ServiceResult<T> Forbidden() => new(StatusCodes.Status403Forbidden);
    public static ServiceResult<T> NotFound(string message) => new(StatusCodes.Status404NotFound, default, message);
    public static ServiceResult<T> Conflict(string message) => new(StatusCodes.Status409Conflict, default, message);
}
