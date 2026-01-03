namespace WALLEve.Services.Sde.Interfaces;

public interface ISdeNstService
{
    Task<string> GetTypeNameAsync(int typeId);
}