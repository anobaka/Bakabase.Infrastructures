namespace Bakabase.Infrastructures.Components.App.Relocation
{
    public enum RelocationPhase
    {
        Starting = 0,
        Copying = 1,
        Validating = 2,
        Replacing = 3,
        Finalizing = 4,
        Done = 5,
    }

    public sealed class RelocationProgress
    {
        public RelocationPhase Phase { get; set; }
        public long ProcessedFiles { get; set; }
        public long TotalFiles { get; set; }
        public long ProcessedBytes { get; set; }
        public long TotalBytes { get; set; }
        public string? CurrentFile { get; set; }

        public double FilesFraction => TotalFiles == 0 ? 0d : (double)ProcessedFiles / TotalFiles;
    }
}
