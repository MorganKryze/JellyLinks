using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Notify;
using Jellyfin.Plugin.JellyLinks.Policy;
using Jellyfin.Plugin.JellyLinks.Serving;
using Jellyfin.Plugin.JellyLinks.Tracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.JellyLinks.Api;

[ApiController]
[Route("JellyLinks/f")]
[AllowAnonymous]
public sealed class FileController : ControllerBase
{
    private const long FlushEvery = 64L * 1024 * 1024;

    private readonly FileGate _gate;
    private readonly SessionTracker _tracker;
    private readonly LinkStore _store;
    private readonly Notifier _notifier;
    private readonly Library.ILibraryGateway _library;

    public FileController(FileGate gate, SessionTracker tracker, LinkStore store, Notifier notifier, Library.ILibraryGateway library)
    {
        _gate = gate;
        _tracker = tracker;
        _store = store;
        _notifier = notifier;
        _library = library;
    }

    [HttpGet("{token}/{name}")]
    [HttpHead("{token}/{name}")]
    public async Task Get(string token, string name)
    {
        var isHead = HttpMethods.IsHead(Request.Method);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var gate = await _gate.CheckAsync(token, ip, isHead).ConfigureAwait(false);

        Response.StatusCode = gate.Outcome switch
        {
            GateOutcome.NotFound => StatusCodes.Status404NotFound,
            GateOutcome.Gone => StatusCodes.Status410Gone,
            GateOutcome.Forbidden => StatusCodes.Status403Forbidden,
            GateOutcome.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status200OK,
        };
        if (gate.Outcome != GateOutcome.Serve)
        {
            if (gate.Outcome == GateOutcome.TooManyRequests)
            {
                Response.Headers.RetryAfter = "3600";
            }

            return;
        }

        var link = gate.Link!;
        var batch = gate.Batch!;
        var size = new FileInfo(gate.Path!).Length;
        var range = RangeParser.Parse(Request.Headers.Range, size);

        Response.Headers.AcceptRanges = "bytes";
        Response.ContentType = "application/octet-stream";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = link.FileName,
            FileName = link.FileName,
        }.ToString();

        if (range.Kind == RangeKind.Unsatisfiable)
        {
            Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            Response.Headers.ContentRange = $"bytes */{size}";
            return;
        }

        var length = range.End - range.Start + 1;
        Response.ContentLength = length;
        if (range.Kind == RangeKind.Partial)
        {
            Response.StatusCode = StatusCodes.Status206PartialContent;
            Response.Headers.ContentRange = $"bytes {range.Start}-{range.End}/{size}";
        }

        if (isHead)
        {
            return;
        }

        var session = new SessionBox(_tracker.Begin(link, ip, Request.Headers.UserAgent.ToString(), gate.IsNewIp));
        var cursor = range.Start;
        var completed = false;

        await using var file = new FileStream(gate.Path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        file.Seek(range.Start, SeekOrigin.Begin);
        await CountingCopy.CopyAsync(file, Response.Body, length, FlushEvery, chunk =>
        {
            completed |= _tracker.Record(ref session.Value, link, batch.UserId, cursor, chunk);
            cursor += chunk;
        }, HttpContext.RequestAborted).ConfigureAwait(false);

        if (completed && !batch.CompletedNotified && _store.AllLinksComplete(batch.Id))
        {
            _store.MarkCompletedNotified(batch.Id);
            await _notifier.PublishAsync(new LinkEvent(EventKind.BatchCompleted, batch.UserId, _library.UserName(batch.UserId),
                batch.Label, batch.Id, $"{_store.GetLinks(batch.Id).Count} fichiers reçus en entier")).ConfigureAwait(false);
        }
    }
}

/// <summary>Lets the copy callback update the session record in place (a ref cannot be captured).</summary>
internal sealed class SessionBox(SessionRecord value)
{
    public SessionRecord Value = value;
}
