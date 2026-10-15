using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace ReTray_App.Services;
public sealed class PipeClient
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool mayStart = true;
    public void AllowStart() => mayStart = true;
    public async Task<JsonObject> RequestAsync(string method, JsonObject? data = null, CancellationToken token = default, TimeSpan? timeout = null)
    {
        await gate.WaitAsync(token);
        try
        {
            if (method == "agent.stop") mayStart = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(method=="settings.import"?60:5));
            using var pipe = new NamedPipeClientStream(".", $"ReTray.v1.{Process.GetCurrentProcess().SessionId}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(800, deadline.Token); }
            catch (TimeoutException)
            {
                if (!mayStart || App.IsExiting) throw new IOException("后台已停止。再次启动ReTray即可重新打开。");
                mayStart = false;
                var agent = Path.Combine(AppContext.BaseDirectory, "ReTray.Agent.exe");
                if (!File.Exists(agent)) throw new IOException("后台文件缺失，请使用完整安装包。");
                var started = Process.Start(new ProcessStartInfo(agent, "--background") { UseShellExecute = false, CreateNoWindow = true }) ?? throw new IOException("无法启动后台");
                App.RegisterStartedAgent(started);
                await pipe.ConnectAsync(3000, deadline.Token);
            }
            mayStart = false;
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            var request = new JsonObject { ["schema"] = 1, ["requestId"] = Guid.NewGuid().ToString(), ["method"] = method, ["data"] = data?.DeepClone() ?? new JsonObject() };
            if(timeout.HasValue)request["timeoutMs"]=(int)Math.Clamp(timeout.Value.TotalMilliseconds,1000,60000);
            await writer.WriteLineAsync(request.ToJsonString().AsMemory(), deadline.Token);
            var line = await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("后台已断开连接。");
            return JsonNode.Parse(line)?.AsObject() ?? throw new IOException("后台返回无效数据。");
        }
        finally { gate.Release(); }
    }
}
