using System.Security.Claims;
using InfoGen.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace InfoGen.Client;

// Reads the claims snapshot persisted server-side (PersistingRevalidatingAuthenticationStateProvider)
// via PersistentComponentState. WASM has no access to the HttpOnly auth cookie, so this is the only
// way it learns who's signed in - it never re-validates against the database itself.
internal sealed class PersistentAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> DefaultUnauthenticatedTask =
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

    private readonly Task<AuthenticationState> _authenticationStateTask = DefaultUnauthenticatedTask;

    public PersistentAuthenticationStateProvider(PersistentComponentState state)
    {
        if (!state.TryTakeFromJson<UserInfo>(nameof(UserInfo), out var userInfo) || userInfo is null)
            return;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userInfo.UserId),
            new(ClaimTypes.Name, userInfo.Email ?? userInfo.UserId)
        };
        if (!string.IsNullOrEmpty(userInfo.Email))
            claims.Add(new Claim(ClaimTypes.Email, userInfo.Email));

        _authenticationStateTask = Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(
                new ClaimsIdentity(claims, authenticationType: nameof(PersistentAuthenticationStateProvider)))));
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _authenticationStateTask;
}
