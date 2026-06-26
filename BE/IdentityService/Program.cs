using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var jwtSecret = builder.Configuration["Jwt:SigningKey"] ?? "capstone-review-tool-development-signing-key";

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", async (IdentityDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "identity", status = canConnect ? "healthy" : "database-unavailable" });
});

app.MapPost("/auth/register", async (RegisterRequest request, IdentityDbContext db) =>
{
    var emailExists = await db.Users.AnyAsync(user => user.Email == request.Email);
    if (emailExists)
    {
        return Results.Conflict(new { message = "Email is already registered." });
    }

    var user = UserAccount.Create(request.StudentId, request.FullName, request.Email, request.Password, request.Role);
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/users/{user.Id}", ToUserResponse(user));
});

app.MapPost("/auth/login", async (LoginRequest request, IdentityDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == request.Email);
    if (user is null || !user.IsActive || !VerifyPassword(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
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
        return Results.Unauthorized();
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

app.MapGet("/users", async (IdentityDbContext db) =>
{
    var users = await db.Users
        .AsNoTracking()
        .OrderBy(user => user.FullName)
        .ToListAsync();

    return Results.Ok(users.Select(ToUserResponse));
});

app.MapGet("/users/{id}", async (string id, IdentityDbContext db) =>
{
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
    return user is null ? Results.NotFound() : Results.Ok(ToUserResponse(user));
});

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
});

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
});

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

sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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

record RegisterRequest(string? StudentId, string FullName, string Email, string Password, string Role);
record LoginRequest(string Email, string Password);
record RefreshRequest(string RefreshToken);
record UpdateRoleRequest(string Role);
record UpdateStatusRequest(bool IsActive);
record UserResponse(string Id, string? StudentId, string FullName, string Email, string Role, bool IsActive, DateTime CreatedAt);
record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserResponse User);
