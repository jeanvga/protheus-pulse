using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Application.Security;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Endpoints;

/// <summary>
/// Gestão de contas pela aba Configurações. Antes só existia o administrador criado na
/// primeira tela: qualquer conta adicional exigia mexer no banco.
/// </summary>
public static class UserEndpoints
{
    private const int MaximumUsers = 200;

    public static RouteGroupBuilder MapUsers(this RouteGroupBuilder api)
    {
        api.MapGet("/users", ListAsync).RequireAuthorization("Administrator");
        api.MapPost("/users", CreateAsync).RequireAuthorization("Administrator");
        api.MapPut("/users/{id:guid}", UpdateAsync).RequireAuthorization("Administrator");
        api.MapPost("/users/{id:guid}/password", ResetPasswordAsync).RequireAuthorization("Administrator");
        api.MapDelete("/users/{id:guid}", DeleteAsync).RequireAuthorization("Administrator");
        return api;
    }

    private static async Task<IResult> ListAsync(PulseDbContext dbContext, CancellationToken cancellationToken) =>
        Results.Ok(await dbContext.Users
            .AsNoTracking()
            .OrderBy(item => item.Username)
            .Select(item => new UserResponse(
                item.Id,
                item.Username,
                item.DisplayName,
                item.Email,
                item.Role.ToString(),
                item.IsActive,
                item.CreatedAt,
                item.LastLoginAt))
            .ToListAsync(cancellationToken));

    private static async Task<IResult> CreateAsync(
        SaveUserRequest request,
        PulseDbContext dbContext,
        IPasswordService passwordService,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var username = (request.Username ?? string.Empty).Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (username.Length is 0 or > 120 || username.Any(char.IsWhiteSpace))
        {
            errors["username"] = ["Informe um nome de usuário sem espaços, com até 120 caracteres."];
        }

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
        {
            errors["role"] = ["Perfil inválido."];
        }

        var passwordErrors = PasswordPolicy.Validate(request.Password ?? string.Empty);
        if (passwordErrors.Count > 0)
        {
            errors["password"] = passwordErrors.ToArray();
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (await dbContext.Users.CountAsync(cancellationToken) >= MaximumUsers)
        {
            return Results.BadRequest(new { message = $"Limite de {MaximumUsers} contas atingido." });
        }

        if (await dbContext.Users.AnyAsync(item => item.Username == username, cancellationToken))
        {
            return Results.Conflict(new { message = "Já existe uma conta com esse nome de usuário." });
        }

        var user = new User
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            PasswordHash = passwordService.Hash(request.Password!),
            Role = role,
            IsActive = request.IsActive ?? true,
            CreatedAt = clock.UtcNow
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/users/{user.Id}", new UserResponse(
            user.Id, user.Username, user.DisplayName, user.Email, user.Role.ToString(), user.IsActive, user.CreatedAt, user.LastLoginAt));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        SaveUserRequest request,
        PulseDbContext dbContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Perfil inválido."] });
        }

        var isActive = request.IsActive ?? user.IsActive;
        // Um painel sem administrador ativo não tem como voltar a ter: a tela de gestão
        // exige o perfil que acabaria de ser removido.
        if ((user.Role == UserRole.Administrator && role != UserRole.Administrator) || (user.IsActive && !isActive))
        {
            var remaining = await dbContext.Users.CountAsync(
                item => item.Id != id && item.Role == UserRole.Administrator && item.IsActive,
                cancellationToken);
            if (remaining == 0)
            {
                return Results.BadRequest(new { message = "Esta é a última conta de administrador ativa." });
            }
        }

        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? user.DisplayName : request.DisplayName.Trim();
        user.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        user.Role = role;
        user.IsActive = isActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new UserResponse(
            user.Id, user.Username, user.DisplayName, user.Email, user.Role.ToString(), user.IsActive, user.CreatedAt, user.LastLoginAt));
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequest request,
        PulseDbContext dbContext,
        IPasswordService passwordService,
        CancellationToken cancellationToken)
    {
        var errors = PasswordPolicy.Validate(request.Password ?? string.Empty);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = errors.ToArray() });
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (user is null)
        {
            return Results.NotFound();
        }

        user.PasswordHash = passwordService.Hash(request.Password!);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        PulseDbContext dbContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (string.Equals(principal.FindFirstValue("unique_name"), user.Username, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { message = "Não é possível remover a própria conta." });
        }

        if (user.Role == UserRole.Administrator)
        {
            var remaining = await dbContext.Users.CountAsync(
                item => item.Id != id && item.Role == UserRole.Administrator && item.IsActive,
                cancellationToken);
            if (remaining == 0)
            {
                return Results.BadRequest(new { message = "Esta é a última conta de administrador ativa." });
            }
        }

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    public sealed record SaveUserRequest(
        string? Username,
        string? DisplayName,
        string? Email,
        string? Password,
        string? Role,
        bool? IsActive);

    public sealed record ResetPasswordRequest(string? Password);

    public sealed record UserResponse(
        Guid Id,
        string Username,
        string DisplayName,
        string? Email,
        string Role,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastLoginAt);
}
