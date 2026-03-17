using FaceRecognitionApi.Models;

namespace FaceRecognitionApi.Services;

public interface IFaceRecognitionService
{
    Task<RecognitionResult> RecognizeAsync(Stream imageStream, string fileName);
    Task<string?> AddPersonAsync(string name, Stream imageStream, string fileName);
}
