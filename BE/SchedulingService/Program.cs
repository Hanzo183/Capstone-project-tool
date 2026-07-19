using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Confluent.Kafka;

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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
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
    options.AddPolicy("AdminOnly", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin")));
    options.AddPolicy("ReviewStaff", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Lecturer", "CouncilMember")));
});
builder.Services.AddDbContext<SchedulingDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<SchedulingJobs>();
builder.Services.AddSingleton<IntegrationEventPublisher>();
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        SchemaName = "Hangfire",
        PrepareSchemaIfNecessary = true
    }));
builder.Services.AddHangfireServer();

var app = builder.Build();
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
        logger.LogError(feature?.Error, "Unhandled SchedulingService exception.");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = "An unexpected scheduling service error occurred." });
    });
});
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AdminDashboardAuthorizationFilter() }
});

RecurringJob.AddOrUpdate<SchedulingJobs>(
    "deadline-reminder-job",
    job => job.PublishDeadlineRemindersAsync(),
    "0 */6 * * *");

RecurringJob.AddOrUpdate<SchedulingJobs>(
    "round-status-updater-job",
    job => job.UpdateRoundStatusesAsync(),
    Cron.Daily(0));

app.MapGet("/health", async (SchedulingDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "scheduling", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapPost("/rounds", async (CreateRoundRequest request, SchedulingDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { message = "Round name is required." });
    }

    if (request.EndDate < request.StartDate)
    {
        return Results.BadRequest(new { message = "End date must be on or after start date." });
    }

    if (string.IsNullOrWhiteSpace(request.CreatedBy))
    {
        return Results.BadRequest(new { message = "CreatedBy is required." });
    }

    var round = new ReviewRound
    {
        Id = ShortId.New("RND"),
        Name = request.Name.Trim(),
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        Status = "Upcoming",
        CreatedBy = request.CreatedBy.Trim(),
        CreatedAt = DateTime.UtcNow
    };

    db.ReviewRounds.Add(round);
    await db.SaveChangesAsync();

    return Results.Created($"/rounds/{round.Id}", round);
}).RequireAuthorization("AdminOnly");

app.MapGet("/rounds", async (string? status, string? search, string? sortBy, string? sortDir, int? page, int? pageSize, SchedulingDbContext db) =>
{
    var paging = Paging.Normalize(page, pageSize);
    var query = db.ReviewRounds.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(round => round.Status == status);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.Trim();
        query = query.Where(round => round.Name.Contains(term) || round.Id.Contains(term));
    }

    var totalCount = await query.CountAsync();
    query = RoundHelpers.ApplySort(query, sortBy, sortDir);
    var rounds = await query
        .Skip((paging.Page - 1) * paging.PageSize)
        .Take(paging.PageSize)
        .ToListAsync();

    return Results.Ok(new PagedResult<ReviewRound>(rounds, paging.Page, paging.PageSize, totalCount));
}).RequireAuthorization("Authenticated");

app.MapGet("/rounds/{id}", async (string id, SchedulingDbContext db) =>
{
    var round = await db.ReviewRounds.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    return round is null ? Results.NotFound() : Results.Ok(round);
}).RequireAuthorization("Authenticated");

app.MapPut("/rounds/{id}", async (string id, UpdateRoundRequest request, SchedulingDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { message = "Round name is required." });
    }

    if (request.EndDate < request.StartDate)
    {
        return Results.BadRequest(new { message = "End date must be on or after start date." });
    }

    var round = await db.ReviewRounds.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (round is null)
    {
        return Results.NotFound();
    }

    round.Name = request.Name.Trim();
    round.StartDate = request.StartDate;
    round.EndDate = request.EndDate;
    round.Status = RoundHelpers.NormalizeStatus(request.Status, request.StartDate, request.EndDate);
    await db.SaveChangesAsync();

    return Results.Ok(round);
}).RequireAuthorization("AdminOnly");

app.MapDelete("/rounds/{id}", async (string id, SchedulingDbContext db) =>
{
    var round = await db.ReviewRounds.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (round is null)
    {
        return Results.NotFound();
    }

    var slots = await db.ScheduleSlots.Where(slot => slot.RoundId == id).ToListAsync();
    var slotIds = slots.Select(slot => slot.Id).ToList();
    var reviewers = await db.SlotReviewers.Where(reviewer => slotIds.Contains(reviewer.SlotId)).ToListAsync();
    db.SlotReviewers.RemoveRange(reviewers);
    db.ScheduleSlots.RemoveRange(slots);
    db.ReviewRounds.Remove(round);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

app.MapGet("/rounds/{id}/schedule", async (string id, SchedulingDbContext db) =>
{
    var slots = await db.ScheduleSlots
        .AsNoTracking()
        .Where(slot => slot.RoundId == id)
        .OrderBy(slot => slot.ReviewDate)
        .ToListAsync();

    return Results.Ok(await BuildSlotResponsesAsync(slots, db));
}).RequireAuthorization("Authenticated");

app.MapPost("/schedule/assign", async (AssignSlotRequest request, SchedulingDbContext db, IntegrationEventPublisher events) =>
{
    if (string.IsNullOrWhiteSpace(request.RoundId) ||
        string.IsNullOrWhiteSpace(request.ProjectId) ||
        string.IsNullOrWhiteSpace(request.Room))
    {
        return Results.BadRequest(new { message = "Round, project, and room are required." });
    }

    if (request.DurationMinutes <= 0)
    {
        return Results.BadRequest(new { message = "Duration must be greater than 0 minutes." });
    }

    if (request.CouncilMemberIds is null ||
        request.CouncilMemberIds.Length == 0 ||
        request.CouncilMemberIds.Any(string.IsNullOrWhiteSpace))
    {
        return Results.BadRequest(new { message = "At least one council member is required." });
    }

    var roundExists = await db.ReviewRounds.AnyAsync(round => round.Id == request.RoundId);
    if (!roundExists)
    {
        return Results.BadRequest(new { message = "Review round was not found." });
    }

    var slot = new ScheduleSlot
    {
        Id = ShortId.New("SLT"),
        RoundId = request.RoundId.Trim(),
        ProjectId = request.ProjectId.Trim(),
        ReviewDate = request.ReviewDate,
        Room = request.Room.Trim(),
        DurationMinutes = request.DurationMinutes,
        CreatedAt = DateTime.UtcNow
    };

    var councilMemberIds = request.CouncilMemberIds
        .Select(memberId => memberId.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    db.ScheduleSlots.Add(slot);

    foreach (var memberId in councilMemberIds)
    {
        db.SlotReviewers.Add(new SlotReviewer { SlotId = slot.Id, UserId = memberId });
    }

    await db.SaveChangesAsync();
    await events.PublishAsync("schedule.created", new
    {
        slot.Id,
        slot.RoundId,
        slot.ProjectId,
        slot.ReviewDate,
        slot.Room,
        slot.DurationMinutes,
        councilMemberIds
    });

    return Results.Accepted($"/rounds/{request.RoundId}/schedule", new
    {
        slot = new ScheduleSlotResponse(
            slot.Id,
            slot.RoundId,
            slot.ProjectId,
            slot.ReviewDate,
            slot.Room,
            slot.DurationMinutes,
            councilMemberIds),
        @event = "schedule.created"
    });
}).RequireAuthorization("ReviewStaff");

app.MapGet("/schedule/calendar", async (DateOnly? from, DateOnly? to, SchedulingDbContext db) =>
{
    var query = db.ScheduleSlots.AsNoTracking();

    if (from is not null)
    {
        var fromDate = from.Value.ToDateTime(TimeOnly.MinValue);
        query = query.Where(slot => slot.ReviewDate >= fromDate);
    }

    if (to is not null)
    {
        var toDate = to.Value.ToDateTime(TimeOnly.MaxValue);
        query = query.Where(slot => slot.ReviewDate <= toDate);
    }

    var slots = await query.OrderBy(slot => slot.ReviewDate).ToListAsync();
    return Results.Ok(await BuildSlotResponsesAsync(slots, db));
}).RequireAuthorization("Authenticated");

app.MapGet("/schedule/{id}", async (string id, SchedulingDbContext db) =>
{
    var slot = await db.ScheduleSlots.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (slot is null)
    {
        return Results.NotFound();
    }

    return Results.Ok((await BuildSlotResponsesAsync([slot], db)).First());
}).RequireAuthorization("Authenticated");

app.MapPut("/schedule/{id}", async (string id, AssignSlotRequest request, SchedulingDbContext db, IntegrationEventPublisher events) =>
{
    if (string.IsNullOrWhiteSpace(request.RoundId) ||
        string.IsNullOrWhiteSpace(request.ProjectId) ||
        string.IsNullOrWhiteSpace(request.Room))
    {
        return Results.BadRequest(new { message = "Round, project, and room are required." });
    }

    if (request.DurationMinutes <= 0)
    {
        return Results.BadRequest(new { message = "Duration must be greater than 0 minutes." });
    }

    if (request.CouncilMemberIds is null ||
        request.CouncilMemberIds.Length == 0 ||
        request.CouncilMemberIds.Any(string.IsNullOrWhiteSpace))
    {
        return Results.BadRequest(new { message = "At least one council member is required." });
    }

    var roundExists = await db.ReviewRounds.AnyAsync(round => round.Id == request.RoundId);
    if (!roundExists)
    {
        return Results.BadRequest(new { message = "Review round was not found." });
    }

    var slot = await db.ScheduleSlots.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (slot is null)
    {
        return Results.NotFound();
    }

    slot.RoundId = request.RoundId.Trim();
    slot.ProjectId = request.ProjectId.Trim();
    slot.ReviewDate = request.ReviewDate;
    slot.Room = request.Room.Trim();
    slot.DurationMinutes = request.DurationMinutes;

    var existingReviewers = await db.SlotReviewers.Where(reviewer => reviewer.SlotId == id).ToListAsync();
    db.SlotReviewers.RemoveRange(existingReviewers);
    var councilMemberIds = request.CouncilMemberIds
        .Select(memberId => memberId.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    foreach (var memberId in councilMemberIds)
    {
        db.SlotReviewers.Add(new SlotReviewer { SlotId = slot.Id, UserId = memberId });
    }

    await db.SaveChangesAsync();
    await events.PublishAsync("schedule.updated", new
    {
        slot.Id,
        slot.RoundId,
        slot.ProjectId,
        slot.ReviewDate,
        slot.Room,
        slot.DurationMinutes,
        councilMemberIds
    });

    return Results.Ok(new ScheduleSlotResponse(slot.Id, slot.RoundId, slot.ProjectId, slot.ReviewDate, slot.Room, slot.DurationMinutes, councilMemberIds));
}).RequireAuthorization("ReviewStaff");

app.MapDelete("/schedule/{id}", async (string id, SchedulingDbContext db) =>
{
    var slot = await db.ScheduleSlots.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (slot is null)
    {
        return Results.NotFound();
    }

    var reviewers = await db.SlotReviewers.Where(reviewer => reviewer.SlotId == id).ToListAsync();
    db.SlotReviewers.RemoveRange(reviewers);
    db.ScheduleSlots.Remove(slot);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization("ReviewStaff");

app.MapPost("/schedule/jobs/deadline-reminders", async (SchedulingJobs jobs) =>
{
    var result = await jobs.PublishDeadlineRemindersAsync();

    return Results.Accepted("/notifications", new
    {
        job = "DeadlineReminderJob",
        scannedAt = DateTime.UtcNow,
        result.SlotCount,
        @event = "deadline.reminder"
    });
}).RequireAuthorization("AdminOnly");

app.MapPost("/schedule/jobs/round-status", async (SchedulingJobs jobs) =>
{
    var result = await jobs.UpdateRoundStatusesAsync();
    return Results.Ok(new { job = "RoundStatusUpdaterJob", updatedAt = DateTime.UtcNow, result.UpdatedCount });
}).RequireAuthorization("AdminOnly");

app.Run();

static async Task<IEnumerable<ScheduleSlotResponse>> BuildSlotResponsesAsync(List<ScheduleSlot> slots, SchedulingDbContext db)
{
    var slotIds = slots.Select(s => s.Id).ToList();
    var reviewers = await db.SlotReviewers
        .AsNoTracking()
        .Where(sr => slotIds.Contains(sr.SlotId))
        .ToListAsync();

    return slots.Select(slot => new ScheduleSlotResponse(
        slot.Id,
        slot.RoundId,
        slot.ProjectId,
        slot.ReviewDate,
        slot.Room,
        slot.DurationMinutes,
        reviewers.Where(r => r.SlotId == slot.Id).Select(r => r.UserId).ToArray()));
}

static bool HasRole(ClaimsPrincipal user, params string[] roles)
{
    var roleClaims = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role));
    return roleClaims.Any(claim => roles.Any(role => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
}

sealed class SchedulingJobs(SchedulingDbContext db, ILogger<SchedulingJobs> logger, IntegrationEventPublisher events)
{
    public async Task<DeadlineReminderResult> PublishDeadlineRemindersAsync()
    {
        var now = DateTime.UtcNow;
        var upperBound = now.AddHours(48);
        var upcomingSlots = await db.ScheduleSlots
            .AsNoTracking()
            .Where(slot => slot.ReviewDate >= now && slot.ReviewDate <= upperBound)
            .OrderBy(slot => slot.ReviewDate)
            .ToListAsync();

        foreach (var slot in upcomingSlots)
        {
            await events.PublishAsync("deadline.reminder", new
            {
                slot.Id,
                slot.RoundId,
                slot.ProjectId,
                slot.ReviewDate,
                slot.Room,
                slot.DurationMinutes
            });
            logger.LogInformation(
                "deadline.reminder event published for project {ProjectId} in round {RoundId} at {ReviewDate}",
                slot.ProjectId,
                slot.RoundId,
                slot.ReviewDate);
        }

        return new DeadlineReminderResult(upcomingSlots.Count);
    }

    public async Task<RoundStatusUpdateResult> UpdateRoundStatusesAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rounds = await db.ReviewRounds.ToListAsync();

        foreach (var round in rounds)
        {
            round.Status = today < round.StartDate
                ? "Upcoming"
                : today > round.EndDate ? "Closed" : "Active";
        }

        await db.SaveChangesAsync();
        return new RoundStatusUpdateResult(rounds.Count);
    }
}

sealed class AdminDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true && httpContext.User.IsInRole("Admin");
    }
}

sealed class SlotReviewer
{
    public string SlotId { get; set; } = "";
    public string UserId { get; set; } = "";
}

sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public DbSet<ReviewRound> ReviewRounds => Set<ReviewRound>();
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();
    public DbSet<SlotReviewer> SlotReviewers => Set<SlotReviewer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewRound>().ToTable("ReviewRounds").HasKey(round => round.Id);
        modelBuilder.Entity<ScheduleSlot>().ToTable("ScheduleSlots").HasKey(slot => slot.Id);
        modelBuilder.Entity<SlotReviewer>().ToTable("SlotReviewers").HasKey(sr => new { sr.SlotId, sr.UserId });
    }
}

sealed class ReviewRound
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = "Upcoming";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

sealed class ScheduleSlot
{
    public string Id { get; set; } = "";
    public string RoundId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public DateTime ReviewDate { get; set; }
    public string Room { get; set; } = "";
    public int DurationMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
}

static class ShortId
{
    public static string New(string prefix) => $"{prefix}-{RandomNumberGenerator.GetHexString(8)}";
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

static class RoundHelpers
{
    public static IQueryable<ReviewRound> ApplySort(IQueryable<ReviewRound> query, string? sortBy, string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        return ((sortBy ?? "start").Trim().ToLowerInvariant(), descending) switch
        {
            ("name", false) => query.OrderBy(round => round.Name),
            ("name", true) => query.OrderByDescending(round => round.Name),
            ("status", false) => query.OrderBy(round => round.Status),
            ("status", true) => query.OrderByDescending(round => round.Status),
            ("end", false) => query.OrderBy(round => round.EndDate),
            ("end", true) => query.OrderByDescending(round => round.EndDate),
            ("start", true) => query.OrderByDescending(round => round.StartDate),
            _ => query.OrderBy(round => round.StartDate)
        };
    }

    public static string NormalizeStatus(string? requestedStatus, DateOnly startDate, DateOnly endDate)
    {
        string[] allowedStatuses = ["Upcoming", "Active", "Closed"];
        var match = allowedStatuses.FirstOrDefault(status =>
            string.Equals(status, requestedStatus?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return today < startDate ? "Upcoming" : today > endDate ? "Closed" : "Active";
    }
}

record IntegrationEvent(string Name, DateTime OccurredAt, object Payload);

record DeadlineReminderResult(int SlotCount);
record RoundStatusUpdateResult(int UpdatedCount);
record ScheduleSlotResponse(string Id, string RoundId, string ProjectId, DateTime ReviewDate, string Room, int DurationMinutes, string[] CouncilMemberIds);
record CreateRoundRequest(string Name, DateOnly StartDate, DateOnly EndDate, string CreatedBy);
record UpdateRoundRequest(string Name, DateOnly StartDate, DateOnly EndDate, string? Status);
record AssignSlotRequest(string RoundId, string ProjectId, DateTime ReviewDate, string Room, int DurationMinutes, string[] CouncilMemberIds);
record Paging(int Page, int PageSize)
{
    public static Paging Normalize(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, 100));
}

record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
