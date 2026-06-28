using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
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
    options.AddPolicy("ReviewStaff", policy => policy.RequireAssertion(context => HasRole(context.User, "Admin", "Lecturer", "CouncilMember")));
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    context.Request.Headers.Remove("X-User-Id");
    context.Request.Headers.Remove("X-User-Email");
    context.Request.Headers.Remove("X-User-Role");

    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.FindFirstValue("sub");
        var email = context.User.FindFirstValue("email");
        var role = context.User.FindFirstValue("role");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            context.Request.Headers["X-User-Id"] = userId;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            context.Request.Headers["X-User-Email"] = email;
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            context.Request.Headers["X-User-Role"] = role;
        }
    }

    await next();
});
app.MapGet("/health", () => Results.Ok(new { service = "api-gateway", status = "healthy" }));
app.MapReverseProxy();

app.Run();

static bool HasRole(ClaimsPrincipal user, params string[] roles)
{
    var roleClaims = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role));
    return roleClaims.Any(claim => roles.Any(role => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
}
