namespace bot_kit.Application.Interfaces
{
    public interface IBotFactory
    {

        IBot GetBot(string botType);
    }
}
