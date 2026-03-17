namespace FaceRecognitionApp.Models;

public class RecognitionResult
{
    public bool Found { get; set; }
    public Person? Person { get; set; }
    public double Confidence { get; set; }
    public string Message { get; set; } = string.Empty;
}
