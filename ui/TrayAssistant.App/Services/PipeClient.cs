using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace TrayAssistant_App.Services;
public sealed class PipeClient
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool mayStart = true;
    public async Task<JsonObject> RequestAsync(string method, JsonObject? data = null, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (method == "agent.stop") mayStart = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var pipe = new NamedPipeClientStream(".", $"TrayAssistant.v1.{Process.GetCurrentProcess().SessionId}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(800, deadline.Token); }
            catch (TimeoutException)
            {
                if (!mayStart) throw new IOException("后台已停止。再次启动助手即可重新打开。");
                mayStart = false;
                var agent = Path.Combine(AppContext.BaseDirectory, "TrayAssistant.Agent.exe");
                if (!File.Exists(agent)) throw new IOException("后台文件缺失，请使用完整安装包。");
                Process.Start(new ProcessStartInfo(agent, "--background") { UseShellExecute = false, CreateNoWindow = true });
                await pipe.ConnectAsync(3000, deadline.Token);
            }
            mayStart = false;
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            var request = new JsonObject { ["schema"] = 1, ["requestId"] = Guid.NewGuid().ToString(), ["method"] = method, ["data"] = data?.DeepClone() ?? new JsonObject() };
            await writer.WriteLineAsync(request.ToJsonString().AsMemory(), deadline.Token);
            var line = await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("后台已断开连接。");
            return JsonNode.Parse(line)?.AsObject() ?? throw new IOException("后台返回无效数据。");
        }
        finally { gate.Release(); }
    }
}
