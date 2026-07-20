using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
        logger.LogError(feature?.Error, "Unhandled IdentityService exception.");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = "An unexpected identity service error occurred." });
    });
});
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
    var validationError = ValidateRegisterRequest(request);
    if (validationError is not null)
    {
        return Results.BadRequest(new { message = validationError });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var normalizedRole = NormalizeRole(request.Role);
    var normalizedStudentId = NormalizeStudentId(request.StudentId);

    var emailExists = await db.Users.AnyAsync(user => user.Email == normalizedEmail);
    if (emailExists)
    {
        return Results.Conflict(new { message = "Email is already registered." });
    }

    if (!string.IsNullOrWhiteSpace(normalizedStudentId))
    {
        var studentIdExists = await db.Users.AnyAsync(user => user.StudentId == normalizedStudentId || user.Id == normalizedStudentId);
        if (studentIdExists)
        {
            return Results.Conflict(new { message = "Student ID is already registered." });
        }
    }

    var user = UserAccount.Create(normalizedStudentId, request.FullName.Trim(), normalizedEmail, request.Password, normalizedRole);
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
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == normalizedEmail);
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
    if (!IsValidEmail(request.Email))
    {
        return Results.BadRequest(new { message = "A valid email address is required." });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == normalizedEmail);
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
    if (!IsValidEmail(request.Email) || string.IsNullOrWhiteSpace(request.Token))
    {
        return Results.BadRequest(new { message = "Email and reset token are required." });
    }

    var passwordError = ValidatePassword(request.NewPassword);
    if (passwordError is not null)
    {
        return Results.BadRequest(new { message = passwordError });
    }

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == normalizedEmail);
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

app.MapGet("/users", async (string? search, string? role, string? sortBy, string? sortDir, int? page, int? pageSize, IdentityDbContext db) =>
{
    var paging = Paging.Normalize(page, pageSize);
    var query = db.Users.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.Trim();
        query = query.Where(user =>
            user.FullName.Contains(term) ||
            user.Email.Contains(term) ||
            user.Id.Contains(term) ||
            (user.StudentId != null && user.StudentId.Contains(term)));
    }

    if (!string.IsNullOrWhiteSpace(role))
    {
        var normalizedRole = NormalizeRole(role);
        query = query.Where(user => user.Role == normalizedRole);
    }

    var totalCount = await query.CountAsync();
    query = UserSorting.Apply(query, sortBy, sortDir);
    var users = await query
        .Skip((paging.Page - 1) * paging.PageSize)
        .Take(paging.PageSize)
        .ToListAsync();

    return Results.Ok(new PagedResult<UserResponse>(users.Select(ToUserResponse).ToList(), paging.Page, paging.PageSize, totalCount));
}).RequireAuthorization("AdminOnly");

app.MapGet("/users/{id}", async (string id, IdentityDbContext db) =>
{
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    return user is null ? Results.NotFound() : Results.Ok(ToUserResponse(user));
}).RequireAuthorization("AdminOnly");

app.MapPut("/users/{id}/role", async (string id, UpdateRoleRequest request, IdentityDbContext db) =>
{
    if (!IsAllowedRole(request.Role))
    {
        return Results.BadRequest(new { message = "Role must be Student, Lecturer, CouncilMember, or Admin." });
    }

    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (user is null)
    {
        return Results.NotFound();
    }

    user.Role = NormalizeRole(request.Role);
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

app.MapDelete("/users/{id}", async (string id, IdentityDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == id);
    if (user is null)
    {
        return Results.NotFound();
    }

    var refreshTokens = await db.RefreshTokens.Where(token => token.UserId == id).ToListAsync();
    var resetTokens = await db.PasswordResetTokens.Where(token => token.UserId == id).ToListAsync();
    db.RefreshTokens.RemoveRange(refreshTokens);
    db.PasswordResetTokens.RemoveRange(resetTokens);
    db.Users.Remove(user);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

app.Run();

static UserResponse ToUserResponse(UserAccount user) =>
    new(user.Id, user.StudentId, user.FullName, user.Email, user.Role, user.IsActive, user.CreatedAt);

static string? ValidateRegisterRequest(RegisterRequest request)
{
    if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
    {
        return "Full name must be at least 2 characters.";
    }

    if (!IsValidEmail(request.Email))
    {
        return "A valid email address is required.";
    }

    if (!IsAllowedRole(request.Role))
    {
        return "Role must be Student, Lecturer, CouncilMember, or Admin.";
    }

    var normalizedRole = NormalizeRole(request.Role);
    var normalizedStudentId = NormalizeStudentId(request.StudentId);
    if ((normalizedRole == "Student" || normalizedRole == "CouncilMember") && string.IsNullOrWhiteSpace(normalizedStudentId))
    {
        return "ID is required for Student and CouncilMember accounts.";
    }

    if (normalizedRole == "Student" && !IsValidStudentId(normalizedStudentId!))
    {
        return "Student ID must start with 2 letters followed by 6 numbers, for example SE192706.";
    }

    if (normalizedRole == "CouncilMember" && !IsValidCouncilMemberId(normalizedStudentId!))
    {
        return "Council member ID must start with CM followed by 3 numbers, for example CM001.";
    }

    if (!string.IsNullOrWhiteSpace(normalizedStudentId) &&
        normalizedRole is not ("Student" or "CouncilMember"))
    {
        return "ID is only supported for Student and CouncilMember accounts.";
    }

    return ValidatePassword(request.Password);
}

static string? ValidatePassword(string password)
{
    if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
    {
        return "Password must be at least 8 characters long.";
    }

    return null;
}

static bool IsValidEmail(string email) =>
    !string.IsNullOrWhiteSpace(email) &&
    Regex.IsMatch(email.Trim(), "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$");

static bool IsValidStudentId(string studentId) =>
    Regex.IsMatch(studentId, "^[A-Z]{2}\\d{6}$");

static bool IsValidCouncilMemberId(string councilMemberId) =>
    Regex.IsMatch(councilMemberId, "^CM\\d{3}$");

static string? NormalizeStudentId(string? studentId) =>
    string.IsNullOrWhiteSpace(studentId) ? null : studentId.Trim().ToUpperInvariant();

static string NormalizeRole(string role)
{
    string[] allowedRoles = ["Student", "Lecturer", "CouncilMember", "Admin"];
    var match = allowedRoles.FirstOrDefault(allowedRole =>
        string.Equals(allowedRole, role?.Trim(), StringComparison.OrdinalIgnoreCase));
    return match ?? "";
}

static bool IsAllowedRole(string role) => !string.IsNullOrWhiteSpace(NormalizeRole(role));

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
        modelBuilder.Entity<UserAccount>().HasIndex(user => user.StudentId).IsUnique().HasFilter("[StudentId] IS NOT NULL");
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
            Id = studentId is not null &&
                ((role == "Student" && Regex.IsMatch(studentId, "^[A-Z]{2}\\d{6}$")) ||
                 (role == "CouncilMember" && Regex.IsMatch(studentId, "^CM\\d{3}$")))
                    ? studentId
                    : ShortId.New("USR"),
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

static class UserSorting
{
    public static IQueryable<UserAccount> Apply(IQueryable<UserAccount> query, string? sortBy, string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        return ((sortBy ?? "name").Trim().ToLowerInvariant(), descending) switch
        {
            ("email", false) => query.OrderBy(user => user.Email),
            ("email", true) => query.OrderByDescending(user => user.Email),
            ("role", false) => query.OrderBy(user => user.Role),
            ("role", true) => query.OrderByDescending(user => user.Role),
            ("created", false) => query.OrderBy(user => user.CreatedAt),
            ("created", true) => query.OrderByDescending(user => user.CreatedAt),
            ("name", true) => query.OrderByDescending(user => user.FullName),
            _ => query.OrderBy(user => user.FullName)
        };
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
record Paging(int Page, int PageSize)
{
    public static Paging Normalize(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, 100));
}

record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
