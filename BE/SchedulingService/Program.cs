using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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

    return Results.Ok(slots.Select(ToSlotResponse));
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
        CouncilMemberIds = string.Join(',', request.CouncilMemberIds),
        CreatedAt = DateTime.UtcNow
    };

    db.ScheduleSlots.Add(slot);
    await db.SaveChangesAsync();

    return Results.Accepted($"/rounds/{request.RoundId}/schedule", new
    {
        slot = ToSlotResponse(slot),
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
    return Results.Ok(slots.Select(ToSlotResponse));
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

static ScheduleSlotResponse ToSlotResponse(ScheduleSlot slot) =>
    new(
        slot.Id,
        slot.RoundId,
        slot.ProjectId,
        slot.ReviewDate,
        slot.Room,
        slot.CouncilMemberIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public DbSet<ReviewRound> ReviewRounds => Set<ReviewRound>();
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewRound>().ToTable("ReviewRounds").HasKey(round => round.Id);
        modelBuilder.Entity<ScheduleSlot>().ToTable("ScheduleSlots").HasKey(slot => slot.Id);
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
    public string CouncilMemberIds { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

static class ShortId
{
    public static string New(string prefix) => $"{prefix}-{RandomNumberGenerator.GetHexString(8)}";
}

record ScheduleSlotResponse(string Id, string RoundId, string ProjectId, DateTime ReviewDate, string Room, string[] CouncilMemberIds);
record CreateRoundRequest(string Name, DateOnly StartDate, DateOnly EndDate, string CreatedBy);
record AssignSlotRequest(string RoundId, string ProjectId, DateTime ReviewDate, string Room, string[] CouncilMemberIds);
