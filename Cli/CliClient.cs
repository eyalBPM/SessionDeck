using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using WinGrid.Interop;
using WinGrid.Services;

namespace WinGrid.Cli;

/// <summary>
/// CLI mode: same exe launched with arguments (SPEC §4). Attaches to the parent console
/// (WinExe has none of its own), forwards argv to the running instance's pipe, prints the
/// response and returns its exit code. Target run time &lt;100ms — critical for hooks.
/// </summary>
public static class CliClient
{
    private const int ConnectTimeoutMs = 3000;

    public static int Run(string[] args)
    {
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        if (args[0] is "help" or "--help" or "-h" or "/?")
        {
            Console.Out.WriteLine(HelpText);
            return 0;
        }

        var response = Send(args);
        if (response == null)
        {
            Console.Error.WriteLine("wingrid: no running WinGrid instance — start the app first.");
            return 2;
        }

        if (response.Output.Length > 0)
        {
            if (response.ExitCode == 0) Console.Out.WriteLine(response.Output);
            else Console.Error.WriteLine(response.Output);
        }
        return response.ExitCode;
    }

    public static void TrySendActivate() => Send(new[] { "activate" });

    private static PipeResponse? Send(string[] argv)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeServer.PipeName, PipeDirection.InOut);
            client.Connect(ConnectTimeoutMs);

            using var reader = new StreamReader(client, leaveOpen: true);
            using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(new PipeRequest { Argv = argv }));

            string? line = reader.ReadLine();
            if (line == null) return new PipeResponse(1, "no response from WinGrid");
            return JsonSerializer.Deserialize<PipeResponse>(line) ?? new PipeResponse(1, "bad response");
        }
        catch
        {
            return null;
        }
    }

    private static string HelpText => $"""
        WinGrid CLI (v{typeof(CliClient).Assembly.GetName().Version?.ToString(3)})

        wingrid list                          tiles table: id, state, process, color, title, desc
        wingrid add --match "<title regex>" [--process <name>] [--desc "..."] [--color <c>]
        wingrid remove <target>
        wingrid set <target> [--title "..."] [--desc "..."]     empty --title reverts to auto
        wingrid border <target> --color <c>                     static border color
        wingrid border <target> --color <c> --alt <c2> [--interval <ms>]   blinking (default 500ms)
        wingrid border --match "..." --color <c> --auto-add     add the window if not tiled yet
        wingrid focus <target>                activate window in place
        wingrid pin <target>                  move window to the Stage + activate
        wingrid stage --monitor <n> --half left|right | --full | --rect x,y,w,h
        wingrid zone --monitor <n> --half left|right | --full | --off
        wingrid status                        app state: version, zone, stage, tile counts
        wingrid help

        <target> = tile id, or --match "<regex>" on the tile title
        colors   = red, green, orange, blue, gray, yellow, purple, cyan, magenta, white, black, or #RRGGBB
        monitors = 1-based index; --rect is in virtual-screen pixels
        """;
}
