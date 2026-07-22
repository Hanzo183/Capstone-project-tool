using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using UserProfile;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddDbContext<UserProfileDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

app.MapGrpcService<UserProfileLookupService>();
app.MapGet("/health", async (UserProfileDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { service = "user-profile-grpc", status = canConnect ? "healthy" : "database-unavailable" });
});

app.Run();

sealed class UserProfileLookupService(UserProfileDbContext db, ILogger<UserProfileLookupService> logger)
    : UserProfileLookup.UserProfileLookupBase
{
    public override async Task<UserProfileReply> GetUserProfile(UserProfileRequest request, ServerCallContext context)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.UserId || candidate.StudentId == request.UserId, context.CancellationToken);

        return user is null ? new UserProfileReply { Found = false } : ToReply(user);
    }

    public override async Task<StudentValidationReply> ValidateStudent(StudentValidationRequest request, ServerCallContext context)
    {
        var studentId = request.StudentId.Trim().ToUpperInvariant();
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate =>
                candidate.IsActive &&
                candidate.Role == "Student" &&
                (candidate.Id == studentId || candidate.StudentId == studentId),
                context.CancellationToken);

        if (user is null)
        {
            logger.LogInformation("Student validation failed for {StudentId}.", studentId);
            return new StudentValidationReply
            {
                IsValid = false,
                Message = $"Student ID {studentId} was not found as an active student."
            };
        }

        return new StudentValidationReply
        {
            IsValid = true,
            Message = "Student is valid.",
            Profile = ToReply(user)
        };
    }

    private static UserProfileReply ToReply(UserAccount user) => new()
    {
        Found = true,
        UserId = user.Id,
        StudentId = user.StudentId ?? "",
        FullName = user.FullName,
        Email = user.Email,
        Role = user.Role,
        IsActive = user.IsActive
    };
}

sealed class UserProfileDbContext(DbContextOptions<UserProfileDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>().ToTable("Users").HasKey(user => user.Id);
    }
}

sealed class UserAccount
{
    public string Id { get; set; } = "";
    public string? StudentId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
}
