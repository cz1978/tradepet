using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TradePet.App.Runtime;
using TradePet.Core.Localization;

namespace TradePet.App.Localization;

public static class UiLanguagePreference
{
    public static async Task<string> LoadAsync(string databasePath)
    {
        if (!File.Exists(databasePath)) return "zh-CN";
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
            }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value_json FROM settings WHERE scope_key = 'global' AND setting_key = 'desktop';";
            if (await command.ExecuteScalarAsync() is not string json) return "zh-CN";
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "zh-CN";
            return UiText.NormalizeLanguage(document.RootElement.TryGetProperty("uiLanguage", out var language)
                && language.ValueKind == JsonValueKind.String ? language.GetString() : null);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or IOException)
        {
            AppLog.Write($"UI language preference could not be read: {ex.Message}");
            return "zh-CN";
        }
    }
}
