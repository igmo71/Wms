using System.Text.Json;

namespace Wms.Mobile.Services;

internal sealed class SecureStorageMobileSessionStore(ISecureStorage secureStorage)
    : IMobileSessionStore
{
    private const string SessionKey = "wms.mobile.session.v1";

    public async Task<MobileSession?> GetAsync()
    {
        var json = await secureStorage.GetAsync(SessionKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MobileSession>(json);
        }
        catch (JsonException)
        {
            Clear();
            return null;
        }
    }

    public Task SaveAsync(MobileSession session) =>
        secureStorage.SetAsync(SessionKey, JsonSerializer.Serialize(session));

    public void Clear() => secureStorage.Remove(SessionKey);
}
