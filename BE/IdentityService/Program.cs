using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<IntegrationEventPublisher>();

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (IdentityDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "identity", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapPost("/auth/register", async (RegisterRequest request, IdentityDbContext db, IntegrationEventPublisher events) =>
{
    var emailExists = await db.Users.AnyAsync(user => user.Email == request.Email);
    if (emailExists)
    {
        return Results.Conflict(new { message = "Email is already registered." });
    }

    var user = UserAccount.Create(request.StudentId, request.FullName, request.Email, request.Password, request.Role);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    await events.PublishAsync("user.registered", new
    {
        UserId = user.Id,
        user.FullName,
        user.Email,
        user.Role,
        user.CreatedAt
    });

    return Results.Created($"/users/{user.Id}", ToUserResponse(user));
});

app.MapPost("/auth/login", async (LoginRequest request, IdentityDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == request.Email);
    if (user is null || !VerifyPassword(request.Password, user.PasswordHash))
    {
        return Results.Json(
            new { message = "Invalid email or password. Please check your information and try again." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!user.IsActive)
    {
        return Results.Json(
            new { message = "Your account is deactivated. Please contact an administrator." },
            statusCode: StatusCodes.Status403Forbidden);
    }

    var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    db.RefreshTokens.Add(new RefreshToken
    {
        Id = ShortId.New("RT"),
        UserId = user.Id,
        Token = refreshToken,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        IsRevoked = false,
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();

    return Results.Ok(new AuthResponse(
        CreateJwt(user, jwtSecret),
        refreshToken,
        DateTime.UtcNow.AddMinutes(15),
        ToUserResponse(user)));
});

app.MapPost("/auth/logout", async (LogoutRequest request, IdentityDbContext db) =>
{
    var storedRefreshToken = await db.RefreshTokens.FirstOrDefaultAsync(token => token.Token == request.RefreshToken);
    if (storedRefreshToken is not null && !storedRefreshToken.IsRevoked)
    {
        storedRefreshToken.IsRevoked = true;
        await db.SaveChangesAsync();
    }

    return Results.Ok(new { message = "Logged out successfully." });
});

app.MapPost("/auth/refresh", async (RefreshRequest request, IdentityDbContext db) =>
{
    var storedRefreshToken = await db.RefreshTokens
        .Include(token => token.User)
        .FirstOrDefaultAsync(token => token.Token == request.RefreshToken);

    if (storedRefreshToken is null ||
        storedRefreshToken.IsRevoked ||
        storedRefreshToken.ExpiresAt <= DateTime.UtcNow ||
        storedRefreshToken.User is null ||
        !storedRefreshToken.User.IsActive)
    {
        return Results.Json(
            new { message = "Your session has expired. Please sign in again." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    storedRefreshToken.IsRevoked = true;

    var nextRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    db.RefreshTokens.Add(new RefreshToken
    {
        Id = ShortId.New("RT"),
        UserId = storedRefreshToken.UserId,
        Token = nextRefreshToken,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        IsRevoked = false,
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();

    return Results.Ok(new AuthResponse(
        CreateJwt(storedRefreshToken.User, jwtSecret),
        nextRefreshToken,
        DateTime.UtcNow.AddMinutes(15),
        ToUserResponse(storedRefreshToken.User)));
});

app.MapPost("/auth/forgot-password", async (ForgotPasswordRequest request, IdentityDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == request.Email);
    if (user is null || !user.IsActive)
    {
        return Results.Ok(new
        {
            message = "If the email exists, a password reset token has been generated."
        });
    }

    var resetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    db.PasswordResetTokens.Add(new PasswordResetToken
    {
        Id = ShortId.New("PRT"),
        UserId = user.Id,
        Token = resetToken,
        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        IsUsed = false,
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        message = "Password reset token generated. Use it within 30 minutes.",
        resetToken
    });
});

app.MapPost("/auth/reset-password", async (ResetPasswordRequest request, IdentityDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == request.Email);
    if (user is null || !user.IsActive)
    {
        return Results.BadRequest(new { message = "Invalid or expired password reset token." });
    }

    var resetToken = await db.PasswordResetTokens
        .Where(token => token.UserId == user.Id && token.Token == request.Token)
        .OrderByDescending(token => token.CreatedAt)
        .FirstOrDefaultAsync();

    if (resetToken is null || resetToken.IsUsed || resetToken.ExpiresAt <= DateTime.UtcNow)
    {
        return Results.BadRequest(new { message = "Invalid or expired password reset token." });
    }

    user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
    resetToken.IsUsed = true;

    var activeRefreshTokens = await db.RefreshTokens
        .Where(token => token.UserId == user.Id && !token.IsRevoked)
        .ToListAsync();
    foreach (var refreshToken in activeRefreshTokens)
    {
        refreshToken.IsRevoked = true;
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Password has been reset. Please sign in again." });
});

app.MapGet("/users", async (IdentityDbContext db) =>
{
    var users = await db.Users
        .AsNoTracking()
        .OrderBy(user => user.FullName)
        .ToListAsync();

    return Results.Ok(users.Select(ToUserResponse));
}).RequireAuthorization("AdminOnly");

app.MapGet("/users/{id}", async (string id, IdentityDbContext db) =>
{
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    return user is null ? Results.NotFound() : Results.Ok(ToUserResponse(user));
}).RequireAuthorization("AdminOnly");

app.MapPut("/users/{id}/role", async (string id, UpdateRoleRequest request, IdentityDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (user is null)
    {
        return Results.NotFound();
    }

    user.Role = request.Role;
    await db.SaveChangesAsync();

    return Results.Ok(ToUserResponse(user));
}).RequireAuthorization("AdminOnly");

app.MapPut("/users/{id}/status", async (string id, UpdateStatusRequest request, IdentityDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (user is null)
    {
        return Results.NotFound();
    }

    user.IsActive = request.IsActive;
    await db.SaveChangesAsync();

    return Results.Ok(ToUserResponse(user));
}).RequireAuthorization("AdminOnly");

app.Run();

static UserResponse ToUserResponse(UserAccount user) =>
    new(user.Id, user.StudentId, user.FullName, user.Email, user.Role, user.IsActive, user.CreatedAt);

static bool VerifyPassword(string password, string storedHash)
{
    var parts = storedHash.Split('.');
    if (parts.Length != 2)
    {
        return false;
    }

    var salt = Convert.FromBase64String(parts[0]);
    var expectedHash = Convert.FromBase64String(parts[1]);
    var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
    return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
}

static string CreateJwt(UserAccount user, string secret)
{
    var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
    var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
    {
        sub = user.Id,
        email = user.Email,
        role = user.Role,
        name = user.FullName,
        exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(),
        iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    }));
    var unsignedToken = $"{header}.{payload}";

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var signature = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken)));
    return $"{unsignedToken}.{signature}";
}

static string Base64Url(byte[] bytes) =>
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static bool HasRole(ClaimsPrincipal user, params string[] roles)
{
    var roleClaims = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role));
    return roleClaims.Any(claim => roles.Any(role => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
}

sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>().ToTable("Users");
        modelBuilder.Entity<UserAccount>().HasKey(user => user.Id);
        modelBuilder.Entity<UserAccount>().HasIndex(user => user.Email).IsUnique();
        modelBuilder.Entity<RefreshToken>().ToTable("RefreshTokens");
        modelBuilder.Entity<RefreshToken>().HasKey(token => token.Id);
        modelBuilder.Entity<RefreshToken>()
            .HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId);
        modelBuilder.Entity<PasswordResetToken>().ToTable("PasswordResetTokens");
        modelBuilder.Entity<PasswordResetToken>().HasKey(token => token.Id);
        modelBuilder.Entity<PasswordResetToken>().HasIndex(token => token.Token).IsUnique();
        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId);
    }
}

sealed class UserAccount
{
    public string Id { get; set; } = "";
    public string? StudentId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public static UserAccount Create(string? studentId, string fullName, string email, string password, string role) =>
        new()
        {
            Id = studentId?.StartsWith("SE", StringComparison.OrdinalIgnoreCase) == true ? studentId : ShortId.New("USR"),
            StudentId = studentId,
            FullName = fullName,
            Email = email,
            PasswordHash = PasswordHasher.HashPassword(password),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
}

sealed class RefreshToken
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserAccount? User { get; set; }
}

sealed class PasswordResetToken
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserAccount? User { get; set; }
}

static class PasswordHasher
{
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
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

record RegisterRequest(string? StudentId, string FullName, string Email, string Password, string Role);
record LoginRequest(string Email, string Password);
record LogoutRequest(string RefreshToken);
record RefreshRequest(string RefreshToken);
record ForgotPasswordRequest(string Email);
record ResetPasswordRequest(string Email, string Token, string NewPassword);
record UpdateRoleRequest(string Role);
record UpdateStatusRequest(bool IsActive);
record UserResponse(string Id, string? StudentId, string FullName, string Email, string Role, bool IsActive, DateTime CreatedAt);
record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserResponse User);
