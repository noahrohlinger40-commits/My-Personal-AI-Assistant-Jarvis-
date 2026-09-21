using Jarvis.App;
using System.Text;

ApplicationConfiguration.Initialize();

using var cancellationTokenSource = new CancellationTokenSource();

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    if (eventArgs.ExceptionObject is Exception exception)
    {
        StartupCrashLog.Write(exception);
    }
};

Application.ThreadException += (_, eventArgs) =>
{
    if (eventArgs.Exception is not null)
    {
        StartupCrashLog.Write(eventArgs.Exception);
    }
};

try
{
    var context = await AppBootstrapper.CreateAsync(cancellationTokenSource.Token);
    Application.Run(new JarvisMainForm(context));
}
catch (Exception exception)
{
    StartupCrashLog.Write(exception);
    MessageBox.Show(
        exception.Message,
        "Jarvis Startup Failed",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
}

static class StartupCrashLog
{
    public static void Write(Exception exception)
    {
        try
        {
            var root = ResolveRepositoryRoot();

            var dataDirectory = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDirectory);

            var logPath = Path.Combine(dataDirectory, "startup-error.log");
            var builder = new StringBuilder();
            builder.Append('[')
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .AppendLine("]");
            builder.AppendLine(exception.ToString());
            builder.AppendLine();
            File.AppendAllText(logPath, builder.ToString());
        }
        catch
        {
            // Best effort only.
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var currentDirectory = Environment.CurrentDirectory;
        if (Directory.Exists(Path.Combine(currentDirectory, "data")))
        {
            return currentDirectory;
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDirectory, "data")))
        {
            return baseDirectory;
        }

        var parentRoot = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", ".."));
        if (Directory.Exists(Path.Combine(parentRoot, "data")))
        {
            return parentRoot;
        }

        return currentDirectory;
    }
}
