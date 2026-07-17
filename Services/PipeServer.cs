using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace SessionDeck.Services;

public sealed record PipeResponse(int ExitCode, string Output);

/// <summary>
/// Named-pipe server for CLI commands (SPEC §4): one JSON request line per connection
/// ({"argv":[...]}), one JSON response line ({"exitCode":n,"output":"..."}).
/// </summary>
public sealed class PipeServer : IDisposable
{
    public const string PipeName = "sessiondeck";

    private readonly Func<string[], PipeResponse> _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public PipeServer(Func<string[], PipeResponse> handler)
    {
        _handler = handler;
    }

    public void Start() => _loop = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cts.Token);

                using var reader = new StreamReader(server, leaveOpen: true);
                await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };

                string? line = await reader.ReadLineAsync(_cts.Token);
                var response = Handle(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                server.WaitForPipeDrain();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Broken client connection — keep serving.
            }
        }
    }

    private PipeResponse Handle(string? line)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(line))
                return new PipeResponse(1, "empty request");
            var request = JsonSerializer.Deserialize<PipeRequest>(line);
            if (request?.Argv is not { Length: > 0 })
                return new PipeResponse(1, "malformed request");
            return _handler(request.Argv);
        }
        catch (Exception ex)
        {
            return new PipeResponse(1, "error: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

public sealed class PipeRequest
{
    public string[]? Argv { get; set; }
}
