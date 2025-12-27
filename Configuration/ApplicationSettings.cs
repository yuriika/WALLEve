namespace WALLEve.Configuration;

public class ApplicationSettings
{
    public string Name { get; set; } = "WALLEve";
    public string Version { get; set; } = "1.0";
    public string UserAgent { get; set; } = "WALLEve/1.0";
    public string AppDataFolder { get; set; } = "WALLEve";
    public string DataFolder { get; set; } = "Data";
    public ServerSettings Server { get; set; } = new();
}

public class ServerSettings
{
    public string Url { get; set; } = "http://localhost:5000";
}
