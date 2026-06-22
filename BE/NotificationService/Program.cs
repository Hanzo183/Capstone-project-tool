var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

var notifications = new List<NotificationItem>
{
    new(Guid.NewGuid(), "SE192706", "Review slot assigned", "Your review is scheduled for B3-201.", false, DateTimeOffset.UtcNow.AddHours(-6), "schedule.created"),
    new(Guid.NewGuid(), "SE192879", "Submission ready", "Team 6 uploaded architecture-v2.pdf.", false, DateTimeOffset.UtcNow.AddHours(-2), "project.submitted"),
    new(Guid.NewGuid(), "SE192706", "Feedback released", "Council feedback is ready for your project.", true, DateTimeOffset.UtcNow.AddDays(-1), "evaluation.completed")
};

app.MapGet("/health", () => Results.Ok(new { service = "notification", status = "healthy" }));

app.MapGet("/notifications/{userId}", (string userId) =>
{
    var userNotifications = notifications
        .Where(item => item.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(item => item.CreatedAt);

    return Results.Ok(userNotifications);
});

app.MapPut("/notifications/{id:guid}/read", (Guid id) =>
{
    var index = notifications.FindIndex(item => item.Id == id);
    if (index < 0)
    {
        return Results.NotFound();
    }

    notifications[index] = notifications[index] with { IsRead = true };
    return Results.Ok(notifications[index]);
});

app.MapPost("/notify", (CreateNotificationRequest request) =>
{
    var notification = new NotificationItem(
        Guid.NewGuid(),
        request.UserId,
        request.Title,
        request.Body,
        false,
        DateTimeOffset.UtcNow,
        request.Type);

    notifications.Add(notification);
    return Results.Accepted($"/notifications/{request.UserId}", notification);
});

app.Run();

record NotificationItem(Guid Id, string UserId, string Title, string Body, bool IsRead, DateTimeOffset CreatedAt, string Type);
record CreateNotificationRequest(string UserId, string Title, string Body, string Type);
