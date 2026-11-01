using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLedger;

// The mod's own version (from its manifest, as shown in Settings → Mods), and a co-op check: when a run
// starts, each player who has Spire Ledger broadcasts a small hello with its version; a player who sees a
// different version gets a banner ("Bob has Spire Ledger 0.2.0, you 0.2.1").
//
// Network safety: the game numbers message types by sorting their names, mods' included. The message's
// name starts with a lowercase letter so it sorts after every game message: the game's own message ids
// are unchanged, and a teammate without the mod just logs and drops an id it doesn't know.
internal static class ModVersion
{
    private static string? _version;

    public static string Version
    {
        get
        {
            if (_version != null) return _version;
            // Test hook: two local instances share one mods folder, so a mismatch needs a fake version.
            var fake = System.Environment.GetEnvironmentVariable("SPIRELEDGER_FAKE_VERSION");
            if (!string.IsNullOrEmpty(fake)) return _version = fake;
            try { _version = ModManager.GetLoadedMods().FirstOrDefault(m => m.manifest?.id == "SpireLedger")?.manifest?.version; }
            catch { }
            return _version ??= "?";
        }
    }

    // Teammates' versions seen this session, by net id (also written to the run file).
    public static readonly Dictionary<ulong, string> PeerVersions = new();

    private static INetGameService? _registeredOn;
    private static int _sentForPlayers = -1;
    private static string? _banner;
    private static double _bannerUntil;
    private static CanvasLayer? _layer;
    private static Label? _label;
    private static Panel? _panel;

    // Called every frame by the mod's Tick.
    public static void Tick()
    {
        try
        {
            var rm = RunManager.Instance;
            var net = rm.IsInProgress ? rm.NetService : null;
            if (net != null && net.Type.IsMultiplayer())
            {
                if (!ReferenceEquals(net, _registeredOn))
                {
                    net.RegisterMessageHandler<zSpireLedgerHelloMessage>(OnHello);
                    _registeredOn = net;
                    _sentForPlayers = -1;
                    PeerVersions.Clear();
                }
                // (Re)announce when the party changes, e.g. a player (re)joins.
                int players = rm.DebugOnlyGetState()?.Players.Count ?? 0;
                if (players > 1 && players != _sentForPlayers)
                {
                    net.SendMessage(new zSpireLedgerHelloMessage { version = Version });
                    _sentForPlayers = players;
                }
            }
            else _sentForPlayers = -1;
            DrawBanner();
        }
        catch { /* never disrupt the game */ }
    }

    private static void OnHello(zSpireLedgerHelloMessage message, ulong senderId)
    {
        try
        {
            if (senderId == _registeredOn?.NetId) return;
            bool first = !PeerVersions.ContainsKey(senderId);
            PeerVersions[senderId] = message.version ?? "?";
            GD.Print($"[SpireLedger] teammate {senderId} has Spire Ledger {message.version} (ours {Version})");
            // Answer once so the sender learns our version too (they may have announced before we listened).
            if (first) _registeredOn?.SendMessage(new zSpireLedgerHelloMessage { version = Version }, senderId);
            if (message.version != Version)
            {
                string name = RunLedger.PlayerName(senderId) ?? senderId.ToString();
                _banner = Loc.T("version_mismatch", name, message.version ?? "?", Version);
                _bannerUntil = Time.GetTicksMsec() / 1000.0 + 15.0;
            }
        }
        catch { }
    }

    private static void DrawBanner()
    {
        bool show = _banner != null && Time.GetTicksMsec() / 1000.0 < _bannerUntil;
        if (!show) { if (_layer != null) _layer.Visible = false; return; }
        var tree = (SceneTree)Engine.GetMainLoop();
        if (_layer == null)
        {
            _layer = new CanvasLayer { Layer = 128, Name = "SpireLedgerVersionBanner" };
            tree.Root.AddChild(_layer);
            _panel = UiTheme.MakePanel(0.95f, 1f);
            _layer.AddChild(_panel);
            _label = UiTheme.MakeLabel(16, UiTheme.Gold);
            if (GameAssets.Enabled && GameAssets.KreonBold is Font f) _label.AddThemeFontOverride("font", f);
            _layer.AddChild(_label);
        }
        _layer.Visible = true;
        var vp = _layer.GetViewport().GetVisibleRect().Size;
        float s = UiTheme.Scale(vp);
        _label!.Text = "⚠ " + _banner;
        _label.AddThemeFontSizeOverride("font_size", Math.Max(10, (int)Math.Round(17 * s)));
        var size = new Vector2(_label.GetMinimumSize().X + 40f * s, 44f * s);
        var pos = new Vector2((vp.X - size.X) / 2f, 90f * s);
        _panel!.Position = pos; _panel.Size = size;
        _label.Position = pos + new Vector2(20f * s, 9f * s);
    }
}

// Hello message carrying the sender's Spire Ledger version. Lowercase first letter on purpose: see above.
public struct zSpireLedgerHelloMessage : INetMessage
{
    public string version;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;
    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer) => writer.WriteString(version ?? "");
    public void Deserialize(PacketReader reader) => version = reader.ReadString();
}
