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

        public AdminController(IDocumentIngestionService ingestionService)
        {
            _ingestionService = ingestionService;
        }

        [HttpPost("reindex")]
        public async Task<IActionResult> Reindex(CancellationToken cancellationToken)
        {
            await _ingestionService.IngestAsync(cancellationToken);

            return Ok("Reindex completed.");
        }
    }
}
