namespace Rackline.Domain.Common;

internal static class Guard
{
    public static string NotBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} must not be blank.");
        }

        return value.Trim();
    }

    public static Guid NotEmpty(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException($"{name} must not be empty.");
        }

        return value;
    }

    public static int Positive(int value, string name)
    {
        if (value <= 0)
        {
            throw new DomainException($"{name} must be greater than zero.");
        }

        return value;
    }

    public static int NotNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new DomainException($"{name} must not be negative.");
        }

        return value;
    }

    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(message);
        }
    }
}
