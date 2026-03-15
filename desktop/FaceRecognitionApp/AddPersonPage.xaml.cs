using FaceRecognitionApp.Services;

namespace FaceRecognitionApp;

public partial class AddPersonPage : ContentPage
{
    private readonly ApiService _apiService;
    private readonly bool _isDesktop;

    private byte[]? _photoBytes;
    private string _photoFileName = "photo.jpg";

    public AddPersonPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;

        _isDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;
        SelectFileButton.IsVisible = _isDesktop;
        TakePhotoButton.IsVisible = !_isDesktop;
    }

    private async void OnSelectFileClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "wybierz zdjecie osoby",
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
        ResultCard.IsVisible = false;
        UpdateAddButton();
    }

    private void OnNameTextChanged(object sender, TextChangedEventArgs e)
    {
        ResultCard.IsVisible = false;
        UpdateAddButton();
    }

    private void UpdateAddButton()
    {
        AddButton.IsEnabled = _photoBytes != null && !string.IsNullOrWhiteSpace(NameEntry.Text);
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        if (_photoBytes == null || string.IsNullOrWhiteSpace(name)) return;

        SetLoading(true);
        ResultCard.IsVisible = false;

        try
        {
            using var imageStream = new MemoryStream(_photoBytes);
            var person = await _apiService.AddPersonAsync(name, imageStream, _photoFileName);

            ResultStatusLabel.Text = "osoba dodana pomyslnie";
            ResultStatusLabel.TextColor = Color.FromArgb("#3FB950");
            ResultDetailLabel.Text = person != null
                ? $"{person.Name} (ID: {person.Id})"
                : name;
            ResultCard.IsVisible = true;

            NameEntry.Text = string.Empty;
            _photoBytes = null;
            _photoFileName = "photo.jpg";
            PhotoPreview.IsVisible = false;
            PhotoPlaceholder.IsVisible = true;
            UpdateAddButton();
        }
        catch (HttpRequestException ex)
        {
            ResultStatusLabel.Text = "nie udalo sie dodac osoby";
            ResultStatusLabel.TextColor = Color.FromArgb("#F85149");
            ResultDetailLabel.Text = ex.Message;
            ResultCard.IsVisible = true;
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
        AddButton.IsEnabled = !loading && _photoBytes != null && !string.IsNullOrWhiteSpace(NameEntry.Text);
        AddButton.Text = loading ? "zapisywanie" : "dodaj osobe";
    }
}
