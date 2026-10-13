namespace MuxDeck.Application.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
