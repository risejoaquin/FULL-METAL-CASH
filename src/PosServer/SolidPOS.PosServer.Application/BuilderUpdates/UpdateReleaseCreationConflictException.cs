namespace SolidPOS.PosServer.Application.BuilderUpdates;

public sealed class UpdateReleaseCreationConflictException : Exception
{
    public UpdateReleaseCreationConflictException(string errorCode, IReadOnlyCollection<string>? conflictingFields = null)
        : base(errorCode)
    {
        ErrorCode = errorCode;
        ConflictingFields = conflictingFields ?? Array.Empty<string>();
    }

    public string ErrorCode { get; }
    public IReadOnlyCollection<string> ConflictingFields { get; }
}
