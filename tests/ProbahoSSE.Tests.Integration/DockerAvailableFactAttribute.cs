namespace ProbahoSSE.Tests.Integration;

/// <summary>
/// Custom xUnit fact that skips the test when Docker is not available.
/// On CI (GitHub Actions ubuntu-latest) Docker is always present.
/// Locally, start Docker Desktop before running integration tests.
/// </summary>
public sealed class DockerAvailableFactAttribute : FactAttribute
{
    public DockerAvailableFactAttribute()
    {
        if (!IsDockerAvailable())
            Skip = "Docker is not running. Start Docker Desktop to run Redis integration tests.";
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            // Testcontainers checks the Docker socket — we do a lightweight probe.
            var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST")
                             ?? "unix:///var/run/docker.sock";

            // On Windows/Mac Docker Desktop exposes a named pipe / socket.
            // The quickest check: try connecting via the Docker CLI.
            var psi = new System.Diagnostics.ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

