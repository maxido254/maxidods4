using Nefarius.ViGEm.Client;

namespace MaxiDodPS3;

/// <summary>
/// Runs the pad bridge in the background for as long as the app lives. If the ViGEmBus driver is missing
/// (or not loaded yet right after boot) it keeps retrying instead of quitting, so the tray app is always up.
/// </summary>
public sealed class Bridge : IDisposable
{
    private readonly Config _cfg;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;
    private volatile PadManager? _manager;
    private volatile string _status = "Starting...";

    public Bridge(Config cfg) { _cfg = cfg; }

    public Config Config => _cfg;
    public PadManager? Manager => _manager;
    public string Status => _status;

    public void Start() => _task = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        var ct = _cts.Token;

        if (_cfg.Ps3Mode == "native" && _cfg.Ds4Mode == "native" && _cfg.DualSenseMode == "native")
        {
            _status = "Every pad kind is set to native: nothing to do.";
            Log.Warn(_status);
            return;
        }

        ViGEmClient? client = null;
        bool warned = false;
        while (client == null && !ct.IsCancellationRequested)
        {
            try { client = new ViGEmClient(); }
            catch
            {
                _status = "Waiting for the ViGEmBus driver...";
                if (!warned)
                {
                    warned = true;
                    Log.Warn("ViGEmBus driver not found. Install it from https://github.com/nefarius/ViGEmBus/releases (retrying every 5 s).");
                }
                try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { return; }
            }
        }
        if (client == null) return;

        Log.Info($"Bridge running, up to {_cfg.MaxPads} pad(s). DS3 -> {_cfg.Ps3Mode}, DS4 -> {_cfg.Ds4Mode}, DualSense -> {_cfg.DualSenseMode}.");
        _status = "Running";

        try
        {
            var mgr = new PadManager(_cfg, client);
            _manager = mgr;
            await mgr.RunAsync(ct);
        }
        catch (Exception ex) { Log.Error($"Bridge crashed: {ex}"); _status = "Stopped after an error (see log)"; }
        finally { client.Dispose(); _manager = null; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _task?.Wait(5000); } catch { }
        _cts.Dispose();
    }
}
