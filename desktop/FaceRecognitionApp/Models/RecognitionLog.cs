namespace FaceRecognitionApp.Models;

public class RecognitionLog
{
    public int Id { get; set; }
    public DateTime RecognizedAt { get; set; }
    public bool Found { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ImageFileName { get; set; } = string.Empty;
}
