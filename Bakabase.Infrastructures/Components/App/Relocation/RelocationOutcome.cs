namespace Bakabase.Infrastructures.Components.App.Relocation
{
    public enum RelocationOutcomeKind
    {
        NoOp = 0,
        Success = 1,
        Error = 2,
        UnknownSchemaVersion = 3,
    }

    public sealed class RelocationOutcome
    {
        public RelocationOutcomeKind Kind { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }

        public static RelocationOutcome NoOp() => new() { Kind = RelocationOutcomeKind.NoOp };
        public static RelocationOutcome Success() => new() { Kind = RelocationOutcomeKind.Success };

        public static RelocationOutcome Error(System.Exception ex) => new()
        {
            Kind = RelocationOutcomeKind.Error,
            ErrorMessage = ex.Message,
            StackTrace = ex.ToString(),
        };

        public static RelocationOutcome Error(string message) => new()
        {
            Kind = RelocationOutcomeKind.Error,
            ErrorMessage = message,
        };

        public static RelocationOutcome UnknownSchemaVersion(int schemaVersion) => new()
        {
            Kind = RelocationOutcomeKind.UnknownSchemaVersion,
            ErrorMessage = $"Marker schemaVersion={schemaVersion} not understood by this build.",
        };
    }
}
