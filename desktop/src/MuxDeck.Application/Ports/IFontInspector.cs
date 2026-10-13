namespace MuxDeck.Application.Ports;

public interface IFontInspector
{
    Task<FontInspectionResult> InspectAsync(FontInspectionRequest request, ToolCallContext context,
        CancellationToken cancellationToken = default);
}
