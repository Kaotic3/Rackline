namespace Rackline.Application.Common;

/// <summary>The requested resource does not exist.</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;

    public object Key { get; } = key;
}

/// <summary>Someone else changed the same data first; the caller should reload and try again.</summary>
public sealed class ConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The signed-in user is not allowed to do this.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
