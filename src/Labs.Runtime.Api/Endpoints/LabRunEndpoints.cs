using System.Globalization;
using System.Text.Json;
using Labs.Runtime.Api.Contracts;
using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Application.Jobs;

namespace Labs.Runtime.Api.Endpoints;

public static class LabRunEndpoints
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions EventJsonOptions =
        new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapLabRunEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/labs/{labId}/runs", StartAsync)
            .RequireRateLimiting("lab-runs");
        api.MapGet("/runs/{jobId:guid}", GetAsync);
        api.MapGet("/runs/{jobId:guid}/events", StreamEventsAsync);
        api.MapDelete("/runs/{jobId:guid}", CancelAsync);

        return endpoints;
    }

    private static async Task<IResult> StartAsync(
        string labId,
        ILabRunCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await coordinator.StartAsync(labId, cancellationToken);
            var response = LabRunResponse.From(job);
            return Results.Accepted(response.StatusUrl, response);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new
            {
                error = $"Lab '{labId}' was not found."
            });
        }
    }

    private static async Task<IResult> GetAsync(
        Guid jobId,
        ILabJobStore jobStore,
        CancellationToken cancellationToken)
    {
        var job = await jobStore.FindAsync(jobId, cancellationToken);

        return job is null
            ? Results.NotFound(new
            {
                error = $"Lab run '{jobId}' was not found."
            })
            : Results.Ok(LabRunResponse.From(job));
    }

    private static async Task<IResult> CancelAsync(
        Guid jobId,
        ILabJobStore jobStore,
        ILabRunCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var job = await jobStore.FindAsync(jobId, cancellationToken);
        if (job is null)
        {
            return Results.NotFound(new
            {
                error = $"Lab run '{jobId}' was not found."
            });
        }

        if (job.IsTerminal)
        {
            return Results.Conflict(new
            {
                error = $"Lab run '{jobId}' is already complete."
            });
        }

        var cancellationRequested = await coordinator.CancelAsync(
            jobId,
            cancellationToken);

        return cancellationRequested
            ? Results.Accepted($"/api/runs/{jobId}")
            : Results.Conflict(new
            {
                error = $"Lab run '{jobId}' is no longer active."
            });
    }

    private static async Task StreamEventsAsync(
        Guid jobId,
        long? afterSequence,
        ILabJobStore jobStore,
        ILabOutputStore outputStore,
        HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        var job = await jobStore.FindAsync(jobId, cancellationToken);

        if (job is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(
                new { error = $"Lab run '{jobId}' was not found." },
                cancellationToken);
            return;
        }

        var sequence = afterSequence ?? 0;
        if (sequence < 0)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new { error = "afterSequence cannot be negative." },
                cancellationToken);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await context.Response.WriteAsync("retry: 2000\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            await StreamOutputAsync(
                jobId,
                sequence,
                outputStore,
                context.Response,
                cancellationToken);

            var completedJob = await jobStore.FindAsync(
                jobId,
                CancellationToken.None);

            if (completedJob is not null)
            {
                await WriteEventAsync(
                    context.Response,
                    "status",
                    id: null,
                    LabRunResponse.From(completedJob),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser disconnected. The lab continues in the background.
        }
    }

    private static async Task StreamOutputAsync(
        Guid jobId,
        long afterSequence,
        ILabOutputStore outputStore,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        await using var output = outputStore
            .ReadAllAsync(jobId, afterSequence, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            var moveNext = output.MoveNextAsync().AsTask();

            while (!moveNext.IsCompleted)
            {
                var heartbeat = Task.Delay(HeartbeatInterval, cancellationToken);
                if (await Task.WhenAny(moveNext, heartbeat) == moveNext)
                {
                    break;
                }

                await response.WriteAsync(": keep-alive\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }

            if (!await moveNext)
            {
                return;
            }

            var current = output.Current;
            await WriteEventAsync(
                response,
                "output",
                current.Sequence,
                LabOutputResponse.From(current),
                cancellationToken);
        }
    }

    private static async Task WriteEventAsync<T>(
        HttpResponse response,
        string eventName,
        long? id,
        T data,
        CancellationToken cancellationToken)
    {
        if (id.HasValue)
        {
            await response.WriteAsync(
                $"id: {id.Value.ToString(CultureInfo.InvariantCulture)}\n",
                cancellationToken);
        }

        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync(
            $"data: {JsonSerializer.Serialize(data, EventJsonOptions)}\n\n",
            cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
