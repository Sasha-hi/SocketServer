using PowerArgs;

namespace SocketServer
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ServerArgs
    {
        [HelpHook]
        [ArgShortcut("-?")]
        [ArgDescription("Shows this help")]
        // ReSharper disable once UnusedMember.Global
        public bool Help { get; set; }

        [ArgRequired(PromptIfMissing = false)]
        [ArgRange(0, 65535)]
        [ArgShortcut("-p")]
        [ArgDescription("Port number")]
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public int Port { get; set; }
    }
}