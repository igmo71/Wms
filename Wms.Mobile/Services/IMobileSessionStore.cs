namespace Wms.Mobile.Services;

internal interface IMobileSessionStore
{
    Task<MobileSession?> GetAsync();
    Task SaveAsync(MobileSession session);
    void Clear();
}
