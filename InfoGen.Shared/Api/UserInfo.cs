namespace InfoGen.Api;

/// <summary>Minimal claims persisted server-side (PersistentComponentState) and read back by the WASM client's AuthenticationStateProvider.</summary>
public class UserInfo
{
    public string UserId { get; set; } = "";
    public string? Email { get; set; }
}
