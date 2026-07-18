using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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
    options.AddPolicy("ReviewStaff", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Lecturer", "CouncilMember")));
});
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHostedService<KafkaNotificationConsumer>();

var app = builder.Build();
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
        logger.LogError(feature?.Error, "Unhandled NotificationService exception.");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = "An unexpected notification service error occurred." });
    });
});
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (NotificationDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "notification", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapGet("/notifications/{userId}", async (string userId, string? type, bool? unreadOnly, int? page, int? pageSize, NotificationDbContext db) =>
{
    var paging = Paging.Normalize(page, pageSize);
    var query = db.Notifications
        .AsNoTracking()
        .Where(item => item.UserId == userId);

    if (!string.IsNullOrWhiteSpace(type))
    {
        query = query.Where(item => item.Type == type);
    }

    if (unreadOnly == true)
    {
        query = query.Where(item => !item.IsRead);
    }

    var totalCount = await query.CountAsync();
    var userNotifications = await query
        .OrderByDescending(item => item.CreatedAt)
        .Skip((paging.Page - 1) * paging.PageSize)
        .Take(paging.PageSize)
        .ToListAsync();

    return Results.Ok(new PagedResult<NotificationItem>(userNotifications, paging.Page, paging.PageSize, totalCount));
}).RequireAuthorization("Authenticated");

app.MapPut("/notifications/{id}/read", async (string id, NotificationDbContext db) =>
{
    var notification = await db.Notifications.FirstOrDefaultAsync(item => item.Id == id);
    if (notification is null)
    {
        return Results.NotFound();
    }

    notification.IsRead = true;
    await db.SaveChangesAsync();

    return Results.Ok(notification);
}).RequireAuthorization("Authenticated");

app.MapPut("/notifications/{id}", async (string id, UpdateNotificationRequest request, NotificationDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) ||
        string.IsNullOrWhiteSpace(request.Body) ||
        string.IsNullOrWhiteSpace(request.Type))
    {
        return Results.BadRequest(new { message = "Title, body, and type are required." });
    }

    var notification = await db.Notifications.FirstOrDefaultAsync(item => item.Id == id);
    if (notification is null)
    {
        return Results.NotFound();
    }

    notification.Title = request.Title.Trim();
    notification.Body = request.Body.Trim();
    notification.Type = request.Type.Trim();
    notification.IsRead = request.IsRead;
    await db.SaveChangesAsync();

    return Results.Ok(notification);
}).RequireAuthorization("ReviewStaff");

app.MapDelete("/notifications/{id}", async (string id, NotificationDbContext db) =>
{
    var notification = await db.Notifications.FirstOrDefaultAsync(item => item.Id == id);
    if (notification is null)
    {
        return Results.NotFound();
    }

    db.Notifications.Remove(notification);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("Authenticated");

app.MapGet("/notifications/preferences/{userId}", async (string userId, NotificationDbContext db) =>
{
    var preferences = await db.NotificationPreferences
        .AsNoTracking()
        .FirstOrDefaultAsync(item => item.UserId == userId);

    return Results.Ok(preferences ?? NotificationPreference.Default(userId));
}).RequireAuthorization("Authenticated");

app.MapPut("/notifications/preferences/{userId}", async (string userId, UpdateNotificationPreferenceRequest request, NotificationDbContext db) =>
{
    var preferences = await db.NotificationPreferences.FirstOrDefaultAsync(item => item.UserId == userId);
    if (preferences is null)
    {
        preferences = new NotificationPreference { UserId = userId };
        db.NotificationPreferences.Add(preferences);
    }

    preferences.EmailEnabled = request.EmailEnabled;
    preferences.InAppEnabled = request.InAppEnabled;
    preferences.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(preferences);
}).RequireAuthorization("Authenticated");

app.MapPost("/notify", async (CreateNotificationRequest request, NotificationDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.UserId) ||
        string.IsNullOrWhiteSpace(request.Title) ||
        string.IsNullOrWhiteSpace(request.Body) ||
        string.IsNullOrWhiteSpace(request.Type))
    {
        return Results.BadRequest(new { message = "User, title, body, and type are required." });
    }

    var notification = new NotificationItem
    {
        Id = ShortId.New("NOT"),
        UserId = request.UserId.Trim(),
        Title = request.Title.Trim(),
        Body = request.Body.Trim(),
        IsRead = false,
        CreatedAt = DateTime.UtcNow,
        Type = request.Type.Trim()
    };

    db.Notifications.Add(notification);
    await db.SaveChangesAsync();

    return Results.Accepted($"/notifications/{request.UserId}", notification);
}).RequireAuthorization("ReviewStaff");

app.Run();

static bool HasRole(ClaimsPrincipal user, params string[] roles)
{
    var roleClaims = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role));
    return roleClaims.Any(claim => roles.Any(role => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
}

sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationItem> Notifications => Set<NotificationItem>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationItem>().ToTable("Notifications").HasKey(item => item.Id);
        modelBuilder.Entity<NotificationPreference>().ToTable("NotificationPreferences").HasKey(item => item.UserId);
    }
}

sealed class NotificationItem
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Type { get; set; } = "";
}

sealed class NotificationPreference
{
    public string UserId { get; set; } = "";
    public bool EmailEnabled { get; set; } = true;
    public bool InAppEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static NotificationPreference Default(string userId) => new() { UserId = userId };
}

sealed class KafkaNotificationConsumer(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaNotificationConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumerConfig = new ConsumerConfig
                {
                    BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
                    GroupId = configuration["Kafka:GroupId"] ?? "notification-service",
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnableAutoCommit = false
                };

                using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
                consumer.Subscribe(NotificationFactory.SupportedEvents);
                logger.LogInformation("NotificationService consuming Kafka topics: {Topics}.", string.Join(", ", NotificationFactory.SupportedEvents));

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = consumer.Consume(stoppingToken);
                        var integrationEvent = JsonSerializer.Deserialize<IntegrationEventEnvelope>(result.Message.Value, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (integrationEvent is null)
                        {
                            consumer.Commit(result);
                            continue;
                        }

                        await PersistNotificationsAsync(integrationEvent, stoppingToken);
                        consumer.Commit(result);
                    }
                    catch (ConsumeException ex)
                    {
                        logger.LogWarning(ex, "Kafka consume error in NotificationService.");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to process Kafka notification integration event.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kafka notification consumer disconnected. Retrying soon.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PersistNotificationsAsync(IntegrationEventEnvelope integrationEvent, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var notifications = NotificationFactory.Create(integrationEvent).ToList();
        if (notifications.Count == 0)
        {
            return;
        }

        var userIds = notifications.Select(item => item.UserId).Distinct().ToList();
        var preferences = await db.NotificationPreferences
            .Where(item => userIds.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, cancellationToken);

        foreach (var notification in notifications)
        {
            var preference = preferences.GetValueOrDefault(notification.UserId) ?? NotificationPreference.Default(notification.UserId);
            if (!preference.InAppEnabled)
            {
                continue;
            }

            db.Notifications.Add(notification);
            if (preference.EmailEnabled)
            {
                logger.LogInformation("Email notification queued for {UserId}: {Title}", notification.UserId, notification.Title);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

static class NotificationFactory
{
    public static readonly string[] SupportedEvents =
    [
        "project.submitted",
        "schedule.created",
        "schedule.updated",
        "evaluation.completed",
        "evaluation.updated",
        "deadline.reminder",
        "user.registered",
        "rebuttal.submitted",
        "rebuttal.reviewed"
    ];

    public static IEnumerable<NotificationItem> Create(IntegrationEventEnvelope integrationEvent)
    {
        var payload = integrationEvent.Payload;
        return integrationEvent.Name switch
        {
            "user.registered" => CreateOne(payload, "UserId", "Welcome to Capstone Review Tool", "Your account has been created.", integrationEvent.Name),
            "project.submitted" => CreateOne(payload, "LecturerId", "New project submission", $"{Read(payload, "Title", "A project")} uploaded {Read(payload, "FileName", "a file")}.", integrationEvent.Name),
            "schedule.created" => CreateScheduleNotifications(payload, integrationEvent.Name),
            "evaluation.completed" => CreateOne(payload, "StudentId", "Evaluation completed", $"Score released for project {Read(payload, "ProjectId", "unknown")}.", integrationEvent.Name),
            "deadline.reminder" => CreateOne(payload, "ProjectId", "Review deadline reminder", $"Review slot starts at {Read(payload, "ReviewDate", "the scheduled time")}.", integrationEvent.Name),
            "rebuttal.submitted" => CreateOne(payload, "EvaluatorId", "Rebuttal pending review", $"A rebuttal was submitted for project {Read(payload, "ProjectId", "unknown")}.", integrationEvent.Name),
            "rebuttal.reviewed" => CreateOne(payload, "StudentId", "Rebuttal reviewed", BuildRebuttalReviewedBody(payload), integrationEvent.Name),
            _ => []
        };
    }

    private static string BuildRebuttalReviewedBody(JsonElement payload)
    {
        var status = Read(payload, "Status", "reviewed");
        var response = Read(payload, "Response", "");
        return string.IsNullOrWhiteSpace(response)
            ? $"Your rebuttal was {status}."
            : $"Your rebuttal was {status}. Council comment: {response}";
    }

    private static IEnumerable<NotificationItem> CreateScheduleNotifications(JsonElement payload, string type)
    {
        if (!payload.TryGetProperty("CouncilMemberIds", out var members) || members.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return members
            .EnumerateArray()
            .Select(member => member.GetString())
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Select(userId => New(userId!, "Review slot assigned", $"Project {Read(payload, "ProjectId", "unknown")} is scheduled in room {Read(payload, "Room", "TBA")}.", type));
    }

    private static IEnumerable<NotificationItem> CreateOne(JsonElement payload, string userProperty, string title, string body, string type)
    {
        var userId = Read(payload, userProperty, "");
        return string.IsNullOrWhiteSpace(userId) ? [] : [New(userId, title, body, type)];
    }

    private static NotificationItem New(string userId, string title, string body, string type) => new()
    {
        Id = ShortId.New("NOT"),
        UserId = userId,
        Title = title,
        Body = body,
        IsRead = false,
        CreatedAt = DateTime.UtcNow,
        Type = type
    };

    private static string Read(JsonElement payload, string property, string fallback)
    {
        if (!payload.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }
}

static class ShortId
{
    public static string New(string prefix) => $"{prefix}-{RandomNumberGenerator.GetHexString(8)}";
}

record CreateNotificationRequest(string UserId, string Title, string Body, string Type);
record UpdateNotificationRequest(string Title, string Body, string Type, bool IsRead);
record UpdateNotificationPreferenceRequest(bool EmailEnabled, bool InAppEnabled);
record IntegrationEventEnvelope(string Name, DateTime OccurredAt, JsonElement Payload);
record Paging(int Page, int PageSize)
{
    public static Paging Normalize(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, 100));
}

record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
