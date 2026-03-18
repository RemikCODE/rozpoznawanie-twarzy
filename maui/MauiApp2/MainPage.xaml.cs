using Microsoft.Maui.Controls;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MauiApp2;

public partial class MainPage : ContentPage
{
    private const string API_BASE = "http://localhost:5233/api";
    private byte[] photoBytes;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void SelectPhoto_Clicked(object sender, EventArgs e)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions { FileTypes = FilePickerFileType.Images });
        if (result != null)
        {
            var stream = await result.OpenReadAsync();
            photoBytes = new byte[stream.Length];
            await stream.ReadAsync(photoBytes, 0, photoBytes.Length);

            PreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(photoBytes));
            ResultFrame.IsVisible = false;
        }
    }

    private async void Recognize_Clicked(object sender, EventArgs e)
    {
        if (photoBytes == null) return;

        LoadingIndicator.IsRunning = true;
        ResultFrame.IsVisible = false;

        try
        {
            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(photoBytes), "image", "image.jpg");

            var response = await client.PostAsync($"{API_BASE}/Faces/recognize", content);
            var json = await response.Content.ReadAsStringAsync();

            // Parsowanie JSON
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool found = root.GetProperty("found").GetBoolean();
            string name = found && root.TryGetProperty("person", out var person)
                          ? person.GetProperty("name").GetString()
                          : "nieznana";
            double confidence = found ? root.GetProperty("confidence").GetDouble() : 0;
            string message = root.GetProperty("message").GetString();

            // Ustawienie Labeli
            ResultStatusLabel.Text = found ? $"Rozpoznano {name}" : "Nie rozpoznano";
            ResultConfidenceLabel.Text = found ? $"Pewność: {Math.Round(confidence * 100)}%" : "";
            ResultMessageLabel.Text = message;

            ResultFrame.IsVisible = true;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Błąd", ex.Message, "OK");
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
        }
    }
}