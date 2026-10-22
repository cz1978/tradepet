using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using TradePet.Application.Runtime;
using TradePet.Core.Domain;
using TradePet.Core.Localization;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private async Task<ReviewAttachment?> AddEntryScreenshotAsync(TradeKey key, long generation, bool paste)
    {
        string path;
        string title;
        string? temporaryPath = null;
        try
        {
            if (paste)
            {
                var image = System.Windows.Clipboard.ContainsImage() ? System.Windows.Clipboard.GetImage() : null;
                if (image is null) throw new InvalidOperationException("剪贴板中没有可粘贴的图片。");
                temporaryPath = Path.Combine(Path.GetTempPath(), $"TradePet-entry-{Guid.NewGuid():N}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    encoder.Save(stream);
                path = temporaryPath;
                title = UiText.Translate("入场截图") + " " + _timeProvider.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = UiText.Translate("导入入场截图"),
                    Filter = UiText.Translate("截图图片|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp"),
                    CheckFileExists = true,
                    Multiselect = false,
                };
                if (dialog.ShowDialog() != true) return null;
                path = dialog.FileName;
                title = Path.GetFileNameWithoutExtension(path);
            }
            var attachment = await ImportEntryScreenshotAsync(key, generation, path, title);
            if (_accountSessions.IsCurrent(key.AccountKey, generation))
                await RequestCompanionReactionAsync(TradePet.Core.Pet.CompanionTrigger.ScreenshotSaved);
            return attachment;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<ReviewAttachment> ImportEntryScreenshotAsync(TradeKey key, long generation, string path, string title)
    {
        await using var lease = await EnterRuntimeOperationAsync(MaintenanceOperationKind.Write, _cancellation.Token);
        if (!_persistenceAvailable || !_accountSessions.IsCurrent(key.AccountKey, generation))
            throw new InvalidOperationException("账户已切换或存储不可写，内容仍保留。");
        if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp"))
            throw new InvalidDataException("仅支持 PNG、JPG、WebP、GIF 和 BMP 截图。");
        var now = _timeProvider.GetUtcNow();
        var evidence = new ReviewEvidenceStamp(ReviewEvidenceSource.UserBackfill, now, now,
            ReviewTimeBasis.Local, "entry-screenshot-v1");
        return await _reviewAttachmentStore.ImportAsync(key.AccountKey, "trade",
            key.PositionId.ToString(CultureInfo.InvariantCulture), path, title, evidence, _cancellation.Token);
    }
}
