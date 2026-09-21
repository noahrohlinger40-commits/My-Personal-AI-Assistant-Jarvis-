using Jarvis.Core;

namespace Jarvis.App;

internal sealed class JarvisDesktopContext
{
    public JarvisDesktopContext(
        string workspaceRoot,
        JarvisOptions options,
        IDesktopAutomationService desktopAutomation,
        JarvisAssistant assistant,
        ISpeaker speaker,
        IVoiceRuntime voiceRuntime)
    {
        WorkspaceRoot = workspaceRoot;
        Options = options;
        DesktopAutomation = desktopAutomation;
        Assistant = assistant;
        Speaker = speaker;
        VoiceRuntime = voiceRuntime;
    }

    public string WorkspaceRoot { get; }

    public JarvisOptions Options { get; set; }

    public IDesktopAutomationService DesktopAutomation { get; }

    public JarvisAssistant Assistant { get; set; }

    public ISpeaker Speaker { get; set; }

    public IVoiceRuntime VoiceRuntime { get; set; }
}
