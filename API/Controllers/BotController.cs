using bot_kit.Application.DTOs;
using bot_kit.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace bot_kit.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BotController : ControllerBase
    {
        private readonly IBotFactory _botFactory;

        public BotController(IBotFactory botFactory)
        {
            _botFactory = botFactory;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] BotRequest request)
        {
            var bot = _botFactory.GetBot(request.BotType);

            var response = await bot.HandleAsync(request);

            return Ok(response);
        }
    }
}
