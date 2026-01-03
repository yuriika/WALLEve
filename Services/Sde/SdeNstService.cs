using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Sde;

public class SdeNstService : ISdeNstService
{
    private readonly SdeDbContext _context;
    private readonly ILogger<SdeNstService> _logger;

    public SdeNstService(
        SdeDbContext context,
        ILogger<SdeNstService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> GetTypeNameAsync(int typeId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = "SELECT typeName FROM invTypes WHERE typeID = @typeId";
            cmd.Parameters.AddWithValue("@typeId", typeId);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Unknown Type";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting type name for typeId {TypeId}", typeId);
            return "Error";
        }
    }
}