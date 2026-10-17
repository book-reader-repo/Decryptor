using BookDecryptor.Services;

namespace BookDecryptor;

public partial class MainPage : ContentPage
{
    private readonly DecryptionService _svc = new();
    private byte[]? _lastDecryptedBytes;
    private string _lastFileName = "decrypted.txt";

    public MainPage()
    {
        InitializeComponent();
        SchemePicker.SelectedIndexChanged += OnSchemeChanged;
        UpdateKeyPanel();
    }

    private void OnSchemeChanged(object? sender, EventArgs e) => UpdateKeyPanel();

    private void UpdateKeyPanel()
    {
        int i = SchemePicker.SelectedIndex;
        switch (i)
        {
            case 0:
                KeyPanel.IsVisible = false;
                break;
            case 1:
                KeyPanel.IsVisible = true;
                KeyLabel.Text = "Book UID";
                KeyHint.Text = "Example: 3827 — the app adds 99999 as the offset.";
                KeyEntry.Keyboard = Keyboard.Numeric;
                break;
            case 2:
                KeyPanel.IsVisible = false;
                break;
            case 3:
                KeyPanel.IsVisible = true;
                KeyLabel.Text = "Shift key";
                KeyHint.Text = "The exact integer applied to the cipher. No +99999.";
                KeyEntry.Keyboard = Keyboard.Numeric;
                break;
            case 4:
                KeyPanel.IsVisible = true;
                KeyLabel.Text = "XOR key";
                KeyHint.Text = "32-bit integer. Ciphertext must be base64.";
                KeyEntry.Keyboard = Keyboard.Numeric;
                break;
            case 5:
                KeyPanel.IsVisible = false;
                break;
        }
    }

    private async void OnPickClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pick encrypted file"
            });
            if (result == null) return;
            FilePathEntry.Text = result.FullPath ?? result.FileName;
            _lastFileName = Path.GetFileNameWithoutExtension(result.FileName) + ".decrypted" +
                            (Path.GetExtension(result.FileName) is { Length: > 0 } ext ? ext : ".txt");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Pick failed", ex.Message, "OK");
        }
    }

    private async void OnDecryptClicked(object sender, EventArgs e)
    {
        try
        {
            var path = FilePathEntry.Text;
            if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
            {
                await DisplayAlert("No file", "Please pick a file first.", "OK");
                return;
            }

            var content = await File.ReadAllTextAsync(path);
            string decrypted = "";
            string schemeLabel = "";

            switch (SchemePicker.SelectedIndex)
            {
                case 0:
                {
                    int key = _svc.KeyFromFileName(Path.GetFileName(path));
                    schemeLabel = $"filename-key ^(key={key}^)";
                    decrypted = _svc.DecryptWithKey(content, key);
                    break;
                }
                case 1:
                {
                    var uid = KeyEntry.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(uid))
                    {
                        await DisplayAlert("Missing UID", "Enter a book UID.", "OK");
                        return;
                    }
                    int key = _svc.KeyFromBookUid(uid);
                    schemeLabel = $"book-uid key ^(uid={uid}, key={key}^)";
                    decrypted = _svc.DecryptWithKey(content, key);
                    break;
                }
                case 2:
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        Path.GetFileName(path) ?? "",
                        @"^steps?_^(^\d+^)\.html$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        await DisplayAlert("Not a steps file",
                            "Strict step-number mode requires a filename like steps_1.html.", "OK");
                        return;
                    }
                    int step = int.Parse(m.Groups[1].Value);
                    int key = step + DecryptionService.DEFAULT_OFFSET;
                    schemeLabel = $"strict step ^(step={step}, key={key}^)";
                    decrypted = _svc.DecryptWithKey(content, key);
                    break;
                }
                case 3:
                {
                    if (int.TryParse(KeyEntry.Text, out int key))
                    {
                        await DisplayAlert("Bad key", "Enter an integer key.", "OK");
                        return;
                    }
                    schemeLabel = $"raw shift ^(key={key}^)";
                    decrypted = _svc.DecryptRawShift(content, key);
                    break;
                }
                case 4:
                {
                    if (int.TryParse(KeyEntry.Text, out int key))
                    {
                        await DisplayAlert("Bad key", "Enter a numeric XOR key.", "OK");
                        return;
                    }
                    schemeLabel = $"xor ^(key={key}^)";
                    decrypted = _svc.DecryptXor(content, key);
                    break;
                }
                case 5:
                {
                    schemeLabel = "base64";
                    decrypted = _svc.DecryptBase64Only(content);
                    break;
                }
            }

            _lastDecryptedBytes = System.Text.Encoding.UTF8.GetBytes(decrypted);
            PreviewLabel.Text = decrypted.Length > 400 ? decrypted.Substring(0, 400) + "…" : decrypted;
            SaveButton.IsEnabled = _lastDecryptedBytes.Length > 0;
            StatusLabel.Text = $"OK — {schemeLabel}, {decrypted.Length} chars decrypted.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Decrypt failed: {ex.Message}";
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (_lastDecryptedBytes == null || _lastDecryptedBytes.Length == 0) return;
        try
        {
            var target = Path.Combine(FileSystem.AppDataDirectory, _lastFileName);
            await File.WriteAllBytesAsync(target, _lastDecryptedBytes);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Save decrypted file",
                File = new ShareFile(target)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Save failed", ex.Message, "OK");
        }
    }
}
