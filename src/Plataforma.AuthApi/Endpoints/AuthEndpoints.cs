using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using Plataforma.AuthApi.Configuration;
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

        group.MapPost("/forgot-password", RequestPasswordResetAsync)
            .WithName("RequestPasswordReset")
            .WithSummary("Request a password reset email")
            .WithDescription("Returns a generic response so account existence is not disclosed.")
            .Accepts<ForgotPasswordRequest>("application/json")
            .Produces<PasswordResetRequestedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("ResetPassword")
            .WithSummary("Set a new password with a reset token")
            .Accepts<ResetPasswordRequest>("application/json")
            .Produces<PasswordResetResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

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
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return MissingRequestBody();
        }

        var validationErrors = EndpointRequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var email = request.Email.Trim();
        var playerName = request.PlayerName.Trim();
        var normalizedPlayerName = FriendService.NormalizePlayerName(playerName);
        if (await friendService.IsPlayerNameTakenAsync(
            normalizedPlayerName,
            exceptUserId: null,
            cancellationToken))
        {
            return Results.Problem(
                title: "This gamertag is already in use.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            PlayerName = playerName,
            NormalizedPlayerName = normalizedPlayerName
        };
        IdentityResult result;
        try
        {
            result = await userManager.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException)
        {
            if (await friendService.IsPlayerNameTakenAsync(
                normalizedPlayerName,
                exceptUserId: null,
                cancellationToken))
            {
                return Results.Problem(
                    title: "This gamertag is already in use.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            throw;
        }

        if (result.Succeeded)
        {
            return Results.Json(
                new RegisterResponse(user.Id, user.Email!, user.PlayerName!),
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

        var validationErrors = EndpointRequestValidation.Validate(request);
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

        var validationErrors = EndpointRequestValidation.Validate(request);
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

        var validationErrors = EndpointRequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        ForgotPasswordRequest? request,
        UserManager<ApplicationUser> userManager,
        IPasswordResetEmailSender emailSender,
        IOptions<PasswordResetOptions> passwordResetOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return MissingRequestBody();
        }

        var validationErrors = EndpointRequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (!emailSender.IsConfigured
            || !TryGetPasswordResetPageUrl(passwordResetOptions.Value.PublicUrl, out var resetPageUrl))
        {
            return Results.Problem(
                title: "Password reset email delivery is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return PasswordResetRequested();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var resetUrl = QueryHelpers.AddQueryString(
            resetPageUrl,
            new Dictionary<string, string?>
            {
                ["email"] = user.Email,
                ["token"] = encodedToken
            });

        var logger = loggerFactory.CreateLogger("Plataforma.AuthApi.PasswordReset");
        try
        {
            await emailSender.SendPasswordResetAsync(user.Email, resetUrl, cancellationToken);
        }
        catch (Exception exception) when (
            exception is SmtpCommandException
                or SmtpProtocolException
                or IOException
                or SocketException
                or AuthenticationException
                or MailKit.Security.AuthenticationException)
        {
            logger.LogError(exception, "Password reset email delivery failed.");
        }

        return PasswordResetRequested();
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest? request,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return MissingRequestBody();
        }

        var validationErrors = EndpointRequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return InvalidPasswordResetToken();
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (FormatException)
        {
            return InvalidPasswordResetToken();
        }

        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (result.Succeeded)
        {
            await tokenService.RevokeAllRefreshTokensAsync(user.Id, cancellationToken);
            return Results.Ok(new PasswordResetResponse("Your password has been reset."));
        }

        if (result.Errors.Any(error => error.Code == "InvalidToken"))
        {
            return InvalidPasswordResetToken();
        }

        var identityErrors = result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());
        return Results.ValidationProblem(identityErrors);
    }

    private static IResult PasswordResetRequested() =>
        Results.Json(
            new PasswordResetRequestedResponse(
                "If an account exists for this email, a password reset link will be sent."),
            statusCode: StatusCodes.Status202Accepted);

    private static bool TryGetPasswordResetPageUrl(string configuredUrl, out string resetPageUrl)
    {
        resetPageUrl = string.Empty;
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        resetPageUrl = uri.AbsoluteUri;
        return true;
    }

    private static IResult InvalidPasswordResetToken() =>
        Results.Problem(
            title: "The password reset link is invalid or has expired.",
            statusCode: StatusCodes.Status400BadRequest);

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

    private static IResult MissingRequestBody() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["A JSON request body is required."]
        });
}
