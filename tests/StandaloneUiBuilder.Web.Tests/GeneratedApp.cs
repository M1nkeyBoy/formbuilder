using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output;

namespace StandaloneUiBuilder.Web.Tests;

/// <summary>An exported web app, built and running on a free local port until disposed.</summary>
public sealed class GeneratedApp : IDisposable
{
    private readonly Process process;

    private GeneratedApp(Process process, string url)
    {
        this.process = process;
        Url = url;
    }

    public string Url { get; }

    public static async Task<GeneratedApp> StartAsync(ProjectDocument document, Func<ProjectDocument, string, ExportResult> export)
    {
        var parent = Environment.GetEnvironmentVariable("UIB_EXPORT_DIR") is { Length: > 0 } exportDir
            ? Path.Combine(exportDir, "blazor")
            : Directory.CreateTempSubdirectory("uib-web-").FullName;
        var folder = export(document, parent).ProjectFolder;

        var build = Run("dotnet", ["build", folder, "--nologo"]);
        Assert.True(build.ExitCode == 0, "The generated web project did not build:\n" + build.Output);

        var port = FreePort();
        var url = $"http://127.0.0.1:{port}";
        var start = new ProcessStartInfo("dotnet", ["run", "--no-build", "--no-launch-profile", "--project", folder, "--urls", url])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        var process = Process.Start(start)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var app = new GeneratedApp(process, url);
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                if ((await client.GetAsync(url)).StatusCode == HttpStatusCode.OK)
                {
                    return app;
                }
            }
            catch (HttpRequestException)
            {
            }

            if (process.HasExited || DateTime.UtcNow > deadline)
            {
                app.Dispose();
                throw new InvalidOperationException($"The generated web app did not start at {url}.");
            }

            await Task.Delay(500);
        }
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.Dispose();
    }

    private static (int ExitCode, string Output) Run(string file, string[] arguments)
    {
        var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result + error.Result);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
