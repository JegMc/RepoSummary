namespace RepoSummary.Models;

/// <summary>
/// Simple success/failure wrapper so pages can show readable messages
/// instead of catching exceptions or crashing on bad input / API errors.
/// </summary>
public class ServiceResult<T>
{
    public bool Success { get; private set; }
    public T? Value { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static ServiceResult<T> Ok(T value) =>
        new() { Success = true, Value = value };

    public static ServiceResult<T> Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
