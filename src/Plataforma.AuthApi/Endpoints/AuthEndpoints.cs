using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Plataforma.AuthApi.Contracts;
using Plataforma.AuthApi.Services;
using Plataforma.Database.Identity;

namespace Plataforma.AuthApi.Endpoints;

/// <summary>
/// Maps account registration and session endpoints.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Adds the authentication endpoints to the application.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The route group containing the authentication endpoints.</returns>
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/register", RegisterAsync)
            .WithName("RegisterAccount")
            .WithSummary("Register a platform account")
            .WithDescription("Creates an account. Email verification is not configured in this initial version.")
            .Accepts<RegisterRequest>("application/json")
            .Produces<RegisterResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Authenticate an account")
            .WithDescription("Validates the account password and returns an access token and a refresh token.")
            .Accepts<LoginRequest>("application/json")
            .Produces<AuthTokensResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshSession")
            .WithSummary("Rotate a refresh token")
            .WithDescription("Consumes the supplied refresh token and returns a replacement token pair.")
            .Accepts<RefreshTokenRequest>("application/json")
            .Produces<AuthTokensResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Revoke a refresh token family")
            .Accepts<RefreshTokenRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("Get the current account")
            .RequireAuthorization()
            .Produces<CurrentUserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return group;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest? request,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return MissingRequestBody();
        }

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (result.Succeeded)
        {
            return Results.Json(
                new RegisterResponse(user.Id, user.Email!),
                statusCode: StatusCodes.Status201Created);
        }

        if (result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return Results.Problem(
                title: "An account with this email already exists.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var identityErrors = result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());
        return Results.ValidationProblem(identityErrors);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest? request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return MissingRequestBody();
        }

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Results.Problem(
                title: "Invalid email or password.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            return Results.Problem(
                title: "Invalid email or password.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var tokens = await tokenService.CreateTokenPairAsync(user, cancellationToken);
        return Results.Ok(tokens);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshTokenRequest? request,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return MissingRequestBody();
        }

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var tokens = await tokenService.RotateRefreshTokenAsync(request.RefreshToken, cancellationToken);
        return tokens is null
            ? Results.Problem(
                title: "The refresh token is invalid, expired, or has already been used.",
                statusCode: StatusCodes.Status401Unauthorized)
            : Results.Ok(tokens);
    }

    private static async Task<IResult> LogoutAsync(
        RefreshTokenRequest? request,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return MissingRequestBody();
        }

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static IResult GetCurrentUser(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Results.Unauthorized();
        }

        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
        return Results.Ok(new CurrentUserResponse(userId, email ?? string.Empty));
    }

    private static Dictionary<string, string[]> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(memberName => new { MemberName = memberName, result.ErrorMessage }))
            .GroupBy(result => result.MemberName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(result => result.ErrorMessage ?? "The value is invalid.").ToArray());
    }

    private static IResult MissingRequestBody() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["A JSON request body is required."]
        });
}
