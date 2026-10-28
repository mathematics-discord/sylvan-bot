namespace SylvanBot.Application.Common;

/// <summary>An expected operation failure returned to the caller.</summary>
public sealed class OperationFailureException(string code, string description, OperationFailureCategory category) : Exception(description)
{
    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; } = code;

    /// <summary>Gets the failure category.</summary>
    public OperationFailureCategory Category { get; } = category;

    /// <summary>Creates a validation failure.</summary>
    public static OperationFailureException Validation(string code = "General.Validation", string description = "A validation error has occurred.") =>
        new OperationFailureException(code: code, description: description, category: OperationFailureCategory.Validation);

    /// <summary>Creates a conflict failure.</summary>
    public static OperationFailureException Conflict(string code = "General.Conflict", string description = "A conflict error has occurred.") =>
        new OperationFailureException(code: code, description: description, category: OperationFailureCategory.Conflict);

    /// <summary>Creates a notfound failure.</summary>
    public static OperationFailureException NotFound(string code = "General.NotFound", string description = "A 'Not Found' error has occurred.") =>
        new OperationFailureException(code: code, description: description, category: OperationFailureCategory.NotFound);

    /// <summary>Creates a forbidden failure.</summary>
    public static OperationFailureException Forbidden(string code = "General.Forbidden", string description = "A 'Forbidden' error has occurred.") =>
        new OperationFailureException(code: code, description: description, category: OperationFailureCategory.Forbidden);

    /// <summary>Creates a failure failure.</summary>
    public static OperationFailureException Failure(string code = "General.Failure", string description = "A failure has occurred.") =>
        new OperationFailureException(code: code, description: description, category: OperationFailureCategory.Failure);

    /// <summary>Creates a unexpected failure.</summary>
    public static OperationFailureException Unexpected(
        string code = "General.Unexpected",
        string description = "An unexpected error has occurred.") =>
        new OperationFailureException(code: code, description: description, category: OperationFailureCategory.Unexpected);

    /// <summary>Creates a unauthorized failure.</summary>
    public static OperationFailureException Unauthorized(
        string code = "General.Unauthorized",
        string description = "An 'Unauthorized' error has occurred.") =>
        new OperationFailureException(code: code, description: description, category: OperationFailureCategory.Unauthorized);
}