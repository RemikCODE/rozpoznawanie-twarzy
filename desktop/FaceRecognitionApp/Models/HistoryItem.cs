namespace FaceRecognitionApp.Models;

public class HistoryItem
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required Color StatusColor { get; init; }
    public required string TimeText { get; init; }
    public required string Confidence { get; init; }
    public required string Message { get; init; }

    public static HistoryItem From(RecognitionLog log) => new()
    {
        Name = log.Found && !string.IsNullOrWhiteSpace(log.PersonName)
            ? log.PersonName
            : "Unknown",
        Status = log.Found ? "✅  Found" : "❌  Not found",
        StatusColor = log.Found
            ? Color.FromArgb("#2E7D32")
            : Color.FromArgb("#B71C1C"),
        TimeText = log.RecognizedAt.ToLocalTime().ToString("HH:mm  dd/MM/yyyy"),
        Confidence = log.Found && log.Confidence > 0
            ? $"{log.Confidence * 100:F1}%"
            : "—",
        Message = log.Message,
    };
}
