using bot_kit.Application.Interfaces;

namespace bot_kit.Application.Services
{
    public class BotFactory : IBotFactory
    {
        private readonly IEnumerable<IBot> _bots;

        public BotFactory(IEnumerable<IBot> bots)
        {
            _bots = bots;
        }

        public IBot GetBot(string botType)
        {
            if (string.IsNullOrWhiteSpace(botType))
                throw new ArgumentException("BotType is required.");

            var bot = _bots.FirstOrDefault(b =>
                b.BotType.Equals(botType, StringComparison.OrdinalIgnoreCase));

            if (bot == null)
                throw new KeyNotFoundException($"Bot '{botType}' is not registered.");

            return bot;
        }
    }
}
