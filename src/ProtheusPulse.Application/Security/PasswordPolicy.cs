namespace ProtheusPulse.Application.Security;

public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    public static IReadOnlyList<string> Validate(string password)
    {
        var errors = new List<string>();
        if (password.Length < MinimumLength)
        {
            errors.Add($"A senha deve ter ao menos {MinimumLength} caracteres.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("A senha deve conter uma letra maiúscula.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("A senha deve conter uma letra minúscula.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("A senha deve conter um número.");
        }

        if (!password.Any(static character => !char.IsLetterOrDigit(character)))
        {
            errors.Add("A senha deve conter um caractere especial.");
        }

        return errors;
    }
}
