var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

var roundId = Guid.NewGuid();
var rounds = new List<ReviewRound>
{
    new(roundId, "Spring 2025 Round 1", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), "Upcoming", "SE192737")
};

var slots = new List<ScheduleSlot>
{
    new(Guid.NewGuid(), roundId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(3).AddHours(9), "B3-201", new[] { "CM001", "SE192879" }),
    new(Guid.NewGuid(), roundId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(3).AddHours(10), "B3-202", new[] { "CM001", "SE192737" })
};

app.MapGet("/health", () => Results.Ok(new { service = "scheduling", status = "healthy" }));

app.MapPost("/rounds", (CreateRoundRequest request) =>
{
    var round = new ReviewRound(Guid.NewGuid(), request.Name, request.StartDate, request.EndDate, "Upcoming", request.CreatedBy);
    rounds.Add(round);
    return Results.Created($"/rounds/{round.Id}", round);
});

app.MapGet("/rounds", () => Results.Ok(rounds.OrderBy(round => round.StartDate)));

app.MapGet("/rounds/{id:guid}/schedule", (Guid id) =>
{
    var roundSlots = slots.Where(slot => slot.RoundId == id).OrderBy(slot => slot.ReviewDate);
    return Results.Ok(roundSlots);
});

app.MapPost("/schedule/assign", (AssignSlotRequest request) =>
{
    var slot = new ScheduleSlot(Guid.NewGuid(), request.RoundId, request.ProjectId, request.ReviewDate, request.Room, request.CouncilMemberIds);
    slots.Add(slot);

    return Results.Accepted($"/rounds/{request.RoundId}/schedule", new
    {
        slot,
        @event = "schedule.created"
    });
});

app.MapGet("/schedule/calendar", (DateOnly? from, DateOnly? to) =>
{
    var query = slots.AsEnumerable();
    if (from is not null)
    {
        query = query.Where(slot => DateOnly.FromDateTime(slot.ReviewDate.UtcDateTime) >= from);
    }

    if (to is not null)
    {
        query = query.Where(slot => DateOnly.FromDateTime(slot.ReviewDate.UtcDateTime) <= to);
    }

    return Results.Ok(query.OrderBy(slot => slot.ReviewDate));
});

app.MapPost("/schedule/jobs/deadline-reminders", () => Results.Accepted("/notifications", new
{
    job = "DeadlineReminderJob",
    scannedAt = DateTimeOffset.UtcNow,
    @event = "deadline.reminder"
}));

app.MapPost("/schedule/jobs/round-status", () =>
{
    for (var index = 0; index < rounds.Count; index++)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var status = today < rounds[index].StartDate
            ? "Upcoming"
            : today > rounds[index].EndDate ? "Closed" : "Active";

        rounds[index] = rounds[index] with { Status = status };
    }

    return Results.Ok(new { job = "RoundStatusUpdaterJob", updatedAt = DateTimeOffset.UtcNow, rounds });
});

app.Run();

record ReviewRound(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, string Status, string CreatedBy);
record ScheduleSlot(Guid Id, Guid RoundId, Guid ProjectId, DateTimeOffset ReviewDate, string Room, string[] CouncilMemberIds);
record CreateRoundRequest(string Name, DateOnly StartDate, DateOnly EndDate, string CreatedBy);
record AssignSlotRequest(Guid RoundId, Guid ProjectId, DateTimeOffset ReviewDate, string Room, string[] CouncilMemberIds);
