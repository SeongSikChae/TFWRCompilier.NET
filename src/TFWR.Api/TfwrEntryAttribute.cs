namespace TFWR.Api;

/// <summary>
/// Marks a method as the TFWR program entry point for <c>tfwrc</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class TfwrEntryAttribute : Attribute
{
}
