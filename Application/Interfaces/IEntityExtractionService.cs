namespace bot_kit.Application.Interfaces
{
    

        public interface IEntityExtractionService
        {
            Task ExtractAndStoreAsync(
                Guid documentId,
                string content);
        }
    
}
