namespace bot_kit.Application.Interfaces
{
    public interface IKnowledgeJsonPreparationService
    {
        Task<KnowledgeJsonPreparationResult> PrepareAsync(
            bool overwrite = false,
            CancellationToken cancellationToken = default);
    }

    public class KnowledgeJsonPreparationResult
    {
        public int ScannedFiles { get; set; }

        public int CreatedFiles { get; set; }

        public int SkippedFiles { get; set; }

        public int FailedFiles { get; set; }

        public List<string> OutputFiles { get; set; } = new();

        public List<string> Messages { get; set; } = new();
    }
}
