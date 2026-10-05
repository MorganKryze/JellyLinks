using Jellyfin.Plugin.JellyLinks.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyLinks.Api;

/// <summary>
/// The client core with its dictionary, for the admin page: it must work even when JavaScript Injector is missing.
/// Public on purpose: it is the same code every user's browser already receives, and it holds no secret.
/// </summary>
[ApiController]
[Route("JellyLinks/client")]
[AllowAnonymous]
public sealed class ClientController : ControllerBase
{
    [HttpGet("core.js")]
    public ContentResult Core() => Content(ClientScript.Load(new[] { "core.js" }), "application/javascript; charset=utf-8");
}
