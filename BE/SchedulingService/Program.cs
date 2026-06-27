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
builder.Services.AddDbContext<SchedulingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", async (SchedulingDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "scheduling", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapPost("/rounds", async (CreateRoundRequest request, SchedulingDbContext db) =>
{
    var round = new ReviewRound
    {
        Id = ShortId.New("RND"),
        Name = request.Name,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        Status = "Upcoming",
        CreatedBy = request.CreatedBy,
        CreatedAt = DateTime.UtcNow
    };

    db.ReviewRounds.Add(round);
    await db.SaveChangesAsync();

    return Results.Created($"/rounds/{round.Id}", round);
});

app.MapGet("/rounds", async (SchedulingDbContext db) =>
{
    var rounds = await db.ReviewRounds
        .AsNoTracking()
        .OrderBy(round => round.StartDate)
        .ToListAsync();

    return Results.Ok(rounds);
});

app.MapGet("/rounds/{id}/schedule", async (string id, SchedulingDbContext db) =>
{
    var slots = await db.ScheduleSlots
        .AsNoTracking()
        .Where(slot => slot.RoundId == id)
        .OrderBy(slot => slot.ReviewDate)
        .ToListAsync();

    var slotIds = slots.Select(s => s.Id).ToList();
    var reviewers = await db.SlotReviewers
        .AsNoTracking()
        .Where(sr => slotIds.Contains(sr.SlotId))
        .ToListAsync();

    var response = slots.Select(slot => new ScheduleSlotResponse(
        slot.Id,
        slot.RoundId,
        slot.ProjectId,
        slot.ReviewDate,
        slot.Room,
        slot.DurationMinutes,
        reviewers.Where(r => r.SlotId == slot.Id).Select(r => r.UserId).ToArray()
    ));

    return Results.Ok(response);
});

app.MapPost("/schedule/assign", async (AssignSlotRequest request, SchedulingDbContext db) =>
{
    var slot = new ScheduleSlot
    {
        Id = ShortId.New("SLT"),
        RoundId = request.RoundId,
        ProjectId = request.ProjectId,
        ReviewDate = request.ReviewDate,
        Room = request.Room,
        DurationMinutes = request.DurationMinutes,
        CreatedAt = DateTime.UtcNow
    };

    db.ScheduleSlots.Add(slot);

    foreach (var memberId in request.CouncilMemberIds)
    {
        db.SlotReviewers.Add(new SlotReviewer { SlotId = slot.Id, UserId = memberId });
    }

    await db.SaveChangesAsync();

    return Results.Accepted($"/rounds/{request.RoundId}/schedule", new
    {
        slot = new ScheduleSlotResponse(
            slot.Id,
            slot.RoundId,
            slot.ProjectId,
            slot.ReviewDate,
            slot.Room,
            slot.DurationMinutes,
            request.CouncilMemberIds),
        @event = "schedule.created"
    });
});

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
    
    var slotIds = slots.Select(s => s.Id).ToList();
    var reviewers = await db.SlotReviewers
        .AsNoTracking()
        .Where(sr => slotIds.Contains(sr.SlotId))
        .ToListAsync();

    var response = slots.Select(slot => new ScheduleSlotResponse(
        slot.Id,
        slot.RoundId,
        slot.ProjectId,
        slot.ReviewDate,
        slot.Room,
        slot.DurationMinutes,
        reviewers.Where(r => r.SlotId == slot.Id).Select(r => r.UserId).ToArray()
    ));

    return Results.Ok(response);
});

app.MapPost("/schedule/jobs/deadline-reminders", async (SchedulingDbContext db) =>
{
    var now = DateTime.UtcNow;
    var upperBound = now.AddHours(48);
    var upcomingSlots = await db.ScheduleSlots
        .AsNoTracking()
        .Where(slot => slot.ReviewDate >= now && slot.ReviewDate <= upperBound)
        .OrderBy(slot => slot.ReviewDate)
        .ToListAsync();

    return Results.Accepted("/notifications", new
    {
        job = "DeadlineReminderJob",
        scannedAt = DateTime.UtcNow,
        slotCount = upcomingSlots.Count,
        @event = "deadline.reminder"
    });
});

app.MapPost("/schedule/jobs/round-status", async (SchedulingDbContext db) =>
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

    return Results.Ok(new { job = "RoundStatusUpdaterJob", updatedAt = DateTime.UtcNow, rounds });
});

app.Run();

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

record ScheduleSlotResponse(string Id, string RoundId, string ProjectId, DateTime ReviewDate, string Room, int DurationMinutes, string[] CouncilMemberIds);
record CreateRoundRequest(string Name, DateOnly StartDate, DateOnly EndDate, string CreatedBy);
record AssignSlotRequest(string RoundId, string ProjectId, DateTime ReviewDate, string Room, int DurationMinutes, string[] CouncilMemberIds);
