using bot_kit.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace bot_kit.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly IDocumentIngestionService _ingestionService;

        private readonly IKnowledgeJsonPreparationService _knowledgeJsonPreparationService;

        public AdminController(
            IDocumentIngestionService ingestionService,
            IKnowledgeJsonPreparationService knowledgeJsonPreparationService)
        {
            _ingestionService = ingestionService;
            _knowledgeJsonPreparationService = knowledgeJsonPreparationService;
        }

        [HttpPost("reindex")]
        public async Task<IActionResult> Reindex(CancellationToken cancellationToken)
        {
            await _ingestionService.IngestAsync(cancellationToken);

            return Ok("Reindex completed.");
        }

        [HttpPost("prepare-knowledge-json")]
        public async Task<IActionResult> PrepareKnowledgeJson(
            [FromQuery] bool overwrite,
            CancellationToken cancellationToken)
        {
            var result =
                await _knowledgeJsonPreparationService.PrepareAsync(
                    overwrite,
                    cancellationToken);

            return Ok(result);
        }
    }
}
