using FaceRecognitionApp.Services;

namespace FaceRecognitionApp;

public partial class MainPage : ContentPage
{
    private readonly ApiService _apiService;
    private readonly bool _isDesktop;

    private byte[]? _photoBytes;
    private string _photoFileName = "photo.jpg";

    public MainPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;

        _isDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;
        SelectFileButton.IsVisible = _isDesktop;
        TakePhotoButton.IsVisible = !_isDesktop;
        InstructionLabel.Text = _isDesktop
            ? "wybierz plik z obrazem aby rozpoznac osobe"
            : "zrob zdjecie aparatem aby rozpoznac osobe";
    }

    private async void OnSelectFileClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "wybierz obraz twarzy",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI,       new[] { ".jpg", ".jpeg", ".png", ".bmp" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.image" } },
                    { DevicePlatform.iOS,         new[] { "public.image" } },
                    { DevicePlatform.Android,     new[] { "image/*" } },
                })
            });

            if (result == null) return;

            _photoFileName = result.FileName;
            using var raw = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await raw.CopyToAsync(ms);
            _photoBytes = ms.ToArray();
            ShowPhotoPreview(_photoBytes);
        }
        catch (Exception ex)
        {
            await DisplayAlert("blad", ex.Message, "ok");
        }
    }

    private async void OnTakePhotoClicked(object sender, EventArgs e)
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("niedostepne", "przechwytywanie z aparatu nie jest wspierane na tym urzadzeniu", "ok");
                return;
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo == null) return;

            _photoFileName = photo.FileName;
            using var raw = await photo.OpenReadAsync();
            using var ms = new MemoryStream();
            await raw.CopyToAsync(ms);
            _photoBytes = ms.ToArray();
            ShowPhotoPreview(_photoBytes);
        }
        catch (Exception ex)
        {
            await DisplayAlert("blad", ex.Message, "ok");
        }
    }

    private void ShowPhotoPreview(byte[] bytes)
    {
        PhotoPreview.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        PhotoPreview.IsVisible = true;
        PhotoPlaceholder.IsVisible = false;
        RecognizeButton.IsEnabled = true;
        ResultCard.IsVisible = false;
    }

    private async void OnRecognizeClicked(object sender, EventArgs e)
    {
        if (_photoBytes == null) return;

        SetLoading(true);
        ResultCard.IsVisible = false;

        try
        {
            using var imageStream = new MemoryStream(_photoBytes);
            var result = await _apiService.RecognizeAsync(imageStream, _photoFileName);

            if (result == null)
            {
                await DisplayAlert("blad", "brak odpowiedzi od serwera", "ok");
                return;
            }

            ResultStatusLabel.Text = result.Found ? "rozpoznano" : "nie rozpoznano";
            ResultStatusLabel.TextColor = result.Found
                ? Color.FromArgb("#3FB950")
                : Color.FromArgb("#F85149");
            ResultNameLabel.Text = result.Person?.Name ?? "—";
            ResultConfidenceLabel.Text = result.Found ? $"{result.Confidence * 100:F1}%" : "—";
            ResultMessageLabel.Text = result.Message;
            ResultCard.IsVisible = true;
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlert("blad polaczenia",
                $"nie mozna polaczyc z backendem\n\n{ex.Message}\n\nupewnij sie ze serwer backendu dziala na porcie 5233",
                "ok");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlert("przekroczono limit czasu",
                "rozpoznawanie twarzy przekroczylo limit czasu\n\nprzy pierwszym uruchomieniu serwis ml pobiera wagi modelu ~93 mb i buduje indeks embeddingow dla kazdego zdjecia w datasetcie to moze zajac kilka minut w zaleznosci od rozmiaru datasetu i twojego sprzetu\n\npoczekaj chwile i sprobuj ponownie nastepne zapytania beda znacznie szybsze",
                "ok");
        }
        catch (Exception ex)
        {
            await DisplayAlert("blad", ex.Message, "ok");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool loading)
    {
        LoadingPanel.IsVisible = loading;
        PhotoButtonsRow.IsVisible = !loading;
        RecognizeButton.IsEnabled = !loading && _photoBytes != null;
        RecognizeButton.Text = loading ? "rozpoznawanie" : "rozpoznaj twarz";
    }
}
