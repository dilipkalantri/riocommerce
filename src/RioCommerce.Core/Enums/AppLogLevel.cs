namespace RioCommerce.Core.Enums;

/// <summary>
/// Severity levels for the application log. Matches Microsoft.Extensions.Logging.LogLevel
/// values so that the same numeric column can be used interchangeably between in-memory
/// logging and DB persistence.
/// </summary>
public enum AppLogLevel
{
    /// <summary>Very detailed, fine-grained — usually disabled in production.</summary>
    Trace = 0,
    /// <summary>Internal flow details useful when diagnosing problems.</summary>
    Debug = 1,
    /// <summary>Normal events worth recording — order completed, key generated, etc.</summary>
    Information = 2,
    /// <summary>Something unexpected but the flow recovered or will retry.</summary>
    Warning = 3,
    /// <summary>A failure that aborted the current operation. Most "did it fail?" alerts surface from here.</summary>
    Error = 4,
    /// <summary>System-level problem requiring immediate attention (DB down, dead-lettered after all retries).</summary>
    Critical = 5,
}
