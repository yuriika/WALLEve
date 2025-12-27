using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Sde;
using WALLEve.Services.Sde.Interfaces;
using ICSharpCode.SharpZipLib.BZip2;

namespace WALLEve.Services.Sde;

public class SdeUpdateService : ISdeUpdateService
{
    private readonly EveOnlineSettings _settings;
    private readonly ApplicationSettings _appSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SdeUpdateService> _logger;
    private readonly string _dataPath;
    private SdeStatus? _cachedStatus;
    private const string ChecksumFileName = "sde.checksum";

    public event EventHandler<SdeStatus>? StatusChanged;

    public SdeUpdateService(
        IOptions<EveOnlineSettings> settings,
        IOptions<ApplicationSettings> appSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<SdeUpdateService> logger)
    {
        _settings = settings.Value;
        _appSettings = appSettings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Data-Ordner im App-Verzeichnis
        _dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _appSettings.AppDataFolder,
            _appSettings.DataFolder);

        Directory.CreateDirectory(_dataPath);
        _logger.LogInformation("SDE data path: {Path}", _dataPath);
    }

    public async Task<SdeStatus> GetStatusAsync(bool checkOnline = false)
    {
        var status = new SdeStatus();
        var filePath = Path.Combine(_dataPath, _settings.Sde.LocalFileName);
        status.FilePath = filePath;

        try
        {
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                status.Exists = true;
                status.FileSizeBytes = fileInfo.Length;
                status.LocalFileDate = fileInfo.LastWriteTime;

                // Alter prüfen
                var ageDays = (DateTime.Now - fileInfo.LastWriteTime).TotalDays;
                status.IsOutdated = ageDays > _settings.Sde.MaxAgeDays;

                // Gespeicherte BZ2-Checksum laden (vom letzten Download)
                var checksumFile = Path.Combine(_dataPath, ChecksumFileName);
                if (File.Exists(checksumFile))
                {
                    status.StoredBz2Checksum = (await File.ReadAllTextAsync(checksumFile)).Trim();
                }

                _logger.LogDebug("Local SDE: {Size}, Age: {Age} days, Outdated: {Outdated}",
                    status.FileSizeFormatted, (int)ageDays, status.IsOutdated);
            }
            else
            {
                _logger.LogInformation("SDE file not found at {Path}", filePath);
            }

            // Online-Check wenn gewünscht
            if (checkOnline)
            {
                await CheckRemoteChecksumAsync(status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SDE status");
            status.ErrorMessage = ex.Message;
        }

        _cachedStatus = status;
        return status;
    }

    private async Task CheckRemoteChecksumAsync(SdeStatus status)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("EveApi");

            // Checksum holen
            var response = await client.GetStringAsync(_settings.Sde.ChecksumUrl);
            status.RemoteChecksum = response.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();
            status.LastOnlineCheck = DateTime.Now;

            // Datum der Remote-Datei holen (HEAD Request)
            try
            {
                using var headRequest = new HttpRequestMessage(HttpMethod.Head, _settings.Sde.DownloadUrl);
                using var headResponse = await client.SendAsync(headRequest);

                if (headResponse.Content.Headers.LastModified.HasValue)
                {
                    status.RemoteFileDate = headResponse.Content.Headers.LastModified.Value.LocalDateTime;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch remote file date");
            }

            _logger.LogDebug("Remote checksum: {Checksum}, Remote date: {Date}",
                status.RemoteChecksum, status.RemoteFileDate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch remote checksum");
            status.ErrorMessage = $"Online-Prüfung fehlgeschlagen: {ex.Message}";
        }
    }

    public async Task<bool> RequiresSetupAsync()
    {
        if (!_settings.Sde.RequireSdeForStartup)
        {
            return false;
        }

        var status = await GetStatusAsync(checkOnline: false);
        return !status.Exists || status.IsOutdated;
    }

    public async Task DownloadAsync(IProgress<SdeDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var downloadProgress = new SdeDownloadProgress { Status = "Starte Download..." };
        progress?.Report(downloadProgress);

        var tempFile = Path.Combine(_dataPath, "sde_download.tmp");
        var bz2File = Path.Combine(_dataPath, "sde.sqlite.bz2");
        var finalFile = Path.Combine(_dataPath, _settings.Sde.LocalFileName);

        try
        {
            /// 1. Download
            _logger.LogInformation("Downloading SDE from {Url}", _settings.Sde.DownloadUrl);
            downloadProgress.Status = "Lade herunter...";
            progress?.Report(downloadProgress);

            var client = _httpClientFactory.CreateClient("SdeDownload");

            using var response = await client.GetAsync(_settings.Sde.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            downloadProgress.TotalBytes = totalBytes;

            // Download in eigenen Block, damit der Stream geschlossen wird
            {
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(bz2File, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[81920];
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalBytesRead += bytesRead;

                    downloadProgress.BytesDownloaded = totalBytesRead;
                    downloadProgress.Status = $"Lade herunter... {downloadProgress.ProgressPercent}%";
                    progress?.Report(downloadProgress);
                }

                _logger.LogInformation("Download complete: {Bytes} bytes", totalBytesRead);
            }
            // FileStream ist jetzt geschlossen

            // 2. Entpacken (BZip2)
            downloadProgress.Status = "Entpacke...";
            downloadProgress.BytesDownloaded = 0;
            downloadProgress.TotalBytes = -1;
            progress?.Report(downloadProgress);

            _logger.LogInformation("Extracting BZ2 file...");

            // Entpacken in eigenen Block
            {
                await using var inputStream = new FileStream(bz2File, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var outputStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                BZip2.Decompress(inputStream, outputStream, true);
            }

            // Checksum der heruntergeladenen BZ2 berechnen und speichern
            var bz2Checksum = await CalculateMd5Async(bz2File);
            var checksumFile = Path.Combine(_dataPath, ChecksumFileName);
            await File.WriteAllTextAsync(checksumFile, bz2Checksum);
            _logger.LogInformation("Stored BZ2 checksum: {Checksum}", bz2Checksum);

            // 3. Alte Datei ersetzen
            if (File.Exists(finalFile))
            {
                File.Delete(finalFile);
            }
            File.Move(tempFile, finalFile);

            // 4. Aufräumen
            if (File.Exists(bz2File))
            {
                File.Delete(bz2File);
            }

            downloadProgress.Status = "Abgeschlossen!";
            downloadProgress.IsCompleted = true;
            progress?.Report(downloadProgress);

            _logger.LogInformation("SDE successfully updated: {Path}", finalFile);

            // Status aktualisieren und Event auslösen
            var newStatus = await GetStatusAsync(checkOnline: false);
            StatusChanged?.Invoke(this, newStatus);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SDE download cancelled");
            downloadProgress.Status = "Abgebrochen";
            downloadProgress.HasError = true;
            progress?.Report(downloadProgress);

            // Aufräumen
            CleanupTempFiles(tempFile, bz2File);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading SDE");
            downloadProgress.Status = "Fehler!";
            downloadProgress.HasError = true;
            downloadProgress.ErrorMessage = ex.Message;
            progress?.Report(downloadProgress);

            CleanupTempFiles(tempFile, bz2File);
            throw;
        }
    }

    private void CleanupTempFiles(params string[] files)
    {
        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete temp file: {File}", file);
            }
        }
    }

    private static async Task<string> CalculateMd5Async(string filePath)
    {
        using var md5 = MD5.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        var hash = await md5.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}