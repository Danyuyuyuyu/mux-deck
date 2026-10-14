using MuxDeck.Desktop.Composition;

namespace MuxDeck.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppComposition.Start(args);
}
