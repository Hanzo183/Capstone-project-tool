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
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", async (NotificationDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "notification", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapGet("/notifications/{userId}", async (string userId, NotificationDbContext db) =>
{
    var userNotifications = await db.Notifications
        .AsNoTracking()
        .Where(item => item.UserId == userId)
        .OrderByDescending(item => item.CreatedAt)
        .ToListAsync();

    return Results.Ok(userNotifications);
});

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
});

app.MapPost("/notify", async (CreateNotificationRequest request, NotificationDbContext db) =>
{
    var notification = new NotificationItem
    {
        Id = ShortId.New("NOT"),
        UserId = request.UserId,
        Title = request.Title,
        Body = request.Body,
        IsRead = false,
        CreatedAt = DateTime.UtcNow,
        Type = request.Type
    };

    db.Notifications.Add(notification);
    await db.SaveChangesAsync();

    return Results.Accepted($"/notifications/{request.UserId}", notification);
});

app.Run();

sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationItem> Notifications => Set<NotificationItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationItem>().ToTable("Notifications").HasKey(item => item.Id);
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

static class ShortId
{
    public static string New(string prefix) => $"{prefix}-{RandomNumberGenerator.GetHexString(8)}";
}

record CreateNotificationRequest(string UserId, string Title, string Body, string Type);
