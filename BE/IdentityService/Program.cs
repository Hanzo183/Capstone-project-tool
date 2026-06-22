using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

var users = new List<UserAccount>
{
    UserAccount.Create("SE192737", "Luong Pham Binh Minh", "minh.backend@fpt.edu.vn", "Backend123!", "Admin"),
    UserAccount.Create("SE192706", "Nguyen Chinh Nhan", "nhan.frontend@fpt.edu.vn", "Frontend123!", "Student"),
    UserAccount.Create("SE192879", "Tran Tuan Minh", "minh.jobs@fpt.edu.vn", "Jobs123!", "Lecturer"),
    UserAccount.Create("CM001", "Council Reviewer", "council@fpt.edu.vn", "Council123!", "CouncilMember")
};

var refreshTokens = new Dictionary<string, Guid>();
var jwtSecret = builder.Configuration["Jwt:SigningKey"] ?? "capstone-review-tool-development-signing-key";

app.MapGet("/health", () => Results.Ok(new { service = "identity", status = "healthy" }));

app.MapPost("/auth/register", (RegisterRequest request) =>
{
    if (users.Any(user => user.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Conflict(new { message = "Email is already registered." });
    }

    var user = UserAccount.Create(request.StudentId, request.FullName, request.Email, request.Password, request.Role);
    users.Add(user);

    return Results.Created($"/users/{user.Id}", ToUserResponse(user));
});

app.MapPost("/auth/login", (LoginRequest request) =>
{
    var user = users.FirstOrDefault(candidate => candidate.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase));
    if (user is null || !user.IsActive || !VerifyPassword(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    refreshTokens[refreshToken] = user.Id;

    return Results.Ok(new AuthResponse(
        CreateJwt(user, jwtSecret),
        refreshToken,
        DateTimeOffset.UtcNow.AddMinutes(15),
        ToUserResponse(user)));
});

app.MapPost("/auth/refresh", (RefreshRequest request) =>
{
    if (!refreshTokens.Remove(request.RefreshToken, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = users.FirstOrDefault(candidate => candidate.Id == userId);
    if (user is null || !user.IsActive)
    {
        return Results.Unauthorized();
    }

    var nextRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    refreshTokens[nextRefreshToken] = user.Id;

    return Results.Ok(new AuthResponse(
        CreateJwt(user, jwtSecret),
        nextRefreshToken,
        DateTimeOffset.UtcNow.AddMinutes(15),
        ToUserResponse(user)));
});

app.MapGet("/users", () => Results.Ok(users.Select(ToUserResponse)));

app.MapGet("/users/{id:guid}", (Guid id) =>
{
    var user = users.FirstOrDefault(candidate => candidate.Id == id);
    return user is null ? Results.NotFound() : Results.Ok(ToUserResponse(user));
});

app.MapPut("/users/{id:guid}/role", (Guid id, UpdateRoleRequest request) =>
{
    var user = users.FirstOrDefault(candidate => candidate.Id == id);
    if (user is null)
    {
        return Results.NotFound();
    }

    user.Role = request.Role;
    return Results.Ok(ToUserResponse(user));
});

app.MapPut("/users/{id:guid}/status", (Guid id, UpdateStatusRequest request) =>
{
    var user = users.FirstOrDefault(candidate => candidate.Id == id);
    if (user is null)
    {
        return Results.NotFound();
    }

    user.IsActive = request.IsActive;
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

sealed class UserAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string StudentId { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public required string Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static UserAccount Create(string studentId, string fullName, string email, string password, string role) =>
        new()
        {
            StudentId = studentId,
            FullName = fullName,
            Email = email,
            PasswordHash = PasswordHasher.HashPassword(password),
            Role = role
        };
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

record RegisterRequest(string StudentId, string FullName, string Email, string Password, string Role);
record LoginRequest(string Email, string Password);
record RefreshRequest(string RefreshToken);
record UpdateRoleRequest(string Role);
record UpdateStatusRequest(bool IsActive);
record UserResponse(Guid Id, string StudentId, string FullName, string Email, string Role, bool IsActive, DateTimeOffset CreatedAt);
record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserResponse User);
