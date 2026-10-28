namespace SylvanBot.Application.Common;

/// <summary>Classifies expected operation failures.</summary>
public enum OperationFailureCategory
{
    /// <summary>Validation failure.</summary>
    Validation,

    /// <summary>Conflict failure.</summary>
    Conflict,

    /// <summary>NotFound failure.</summary>
    NotFound,

    /// <summary>Forbidden failure.</summary>
    Forbidden,

    /// <summary>Unauthorized failure.</summary>
    Unauthorized,

    /// <summary>Failure failure.</summary>
    Failure,

    /// <summary>Unexpected failure.</summary>
    Unexpected,
}
