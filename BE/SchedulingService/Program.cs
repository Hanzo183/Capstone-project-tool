using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
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

app.MapGet("/rounds", async (SchedulingDbContext db) =>
{
    var rounds = await db.ReviewRounds
        .AsNoTracking()
        .OrderBy(round => round.StartDate)
        .ToListAsync();

    return Results.Ok(rounds);
}).RequireAuthorization("Authenticated");

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
}).RequireAuthorization("AdminOnly");

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

record DeadlineReminderResult(int SlotCount);
record RoundStatusUpdateResult(int UpdatedCount);
record ScheduleSlotResponse(string Id, string RoundId, string ProjectId, DateTime ReviewDate, string Room, int DurationMinutes, string[] CouncilMemberIds);
record CreateRoundRequest(string Name, DateOnly StartDate, DateOnly EndDate, string CreatedBy);
record AssignSlotRequest(string RoundId, string ProjectId, DateTime ReviewDate, string Room, int DurationMinutes, string[] CouncilMemberIds);
