using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace SpireLedger;

// Co-op party HUD: each player's damage dealt this fight (and share of the party's), always visible in
// combat — next to each player's entry in the game's party list (top left, the default) or above each
// player's character. The party hotkey (config "party_hotkey", default N) cycles list → above
// characters → hidden; the choice is saved as "party_hud". Solo runs show nothing.
public static partial class DamageChartsMod
{
    private enum PartyHudMode { List, Characters, Off }

    private static PartyHudMode _partyHud = PartyHudMode.List;
    private static (Key Key, List<Key> Mods, string Spec, bool Down) _partyToggle = (Key.N, new(), "n", false);

    private static CanvasLayer? _partyLayer;
    private static readonly List<(Panel Panel, Label Label)> _partyCells = new();

    // Party list entries (NMultiplayerPlayerState nodes), refreshed every ~60 frames.
    private static readonly List<Control> _playerStateNodes = new();
    private static long _playerStateLookupFrame = -1000;
    private static Type? _playerStateType;
    private static PropertyInfoCache? _playerStatePlayer;
    private static System.Reflection.FieldInfo? _playerStateHealthBar, _playerStateTop;

    private sealed class PropertyInfoCache
    {
        public readonly System.Reflection.PropertyInfo? Prop;
        public PropertyInfoCache(System.Reflection.PropertyInfo? p) { Prop = p; }
    }

    private static string PartyHudSpec(PartyHudMode m) => m switch
    {
        PartyHudMode.Characters => "characters",
        PartyHudMode.Off => "off",
        _ => "list",
    };

    private static PartyHudMode ParsePartyHud(string? s) => s switch
    {
        "characters" => PartyHudMode.Characters,
        "off" => PartyHudMode.Off,
        _ => PartyHudMode.List,
    };

    private static void HandlePartyToggle()
    {
        if (!Pressed(ref _partyToggle) || !SafeIsInCombat() || _players.Count < 2) return;
        _partyHud = _partyHud switch
        {
            PartyHudMode.List => PartyHudMode.Characters,
            PartyHudMode.Characters => PartyHudMode.Off,
            _ => PartyHudMode.List,
        };
        Toast.Show($"{Loc.T("party_hud_" + PartyHudSpec(_partyHud))}  ({_partyToggle.Spec.ToUpperInvariant()})");
        SaveConfig();
    }

    private static void HidePartyHud()
    {
        if (_partyLayer != null) _partyLayer.Visible = false;
    }

    // Called from the in-combat branch of Tick when no menu/overlay is on top.
    private static void UpdatePartyHud()
    {
        try
        {
            if (_partyHud == PartyHudMode.Off || _players.Count < 2 || _detailVisible) { HidePartyHud(); return; }
            var snap = _tracker.SourceSnapshot();
            long party = 0;
            for (int s = 0; s < snap.PlayerCount && s < snap.PerPlayer.Length; s++) party += snap.PerPlayer[s].DealtTotal;

            if (_partyLayer == null)
            {
                _partyLayer = new CanvasLayer { Layer = 104, Name = "SpireLedgerPartyHud" };
                ((SceneTree)Engine.GetMainLoop()).Root.AddChild(_partyLayer);
            }
            _partyLayer.Visible = true;
            var vp = _partyLayer.GetViewport().GetVisibleRect().Size;
            float sc = UiTheme.Scale(vp) * _uiScale;

            var places = _partyHud == PartyHudMode.Characters ? new[] { true } : new[] { false };
            int cell = 0;
            for (int s = 0; s < _players.Count; s++)
            {
                long dealt = s < snap.PerPlayer.Length ? snap.PerPlayer[s].DealtTotal : 0;
                int pct = party > 0 ? (int)Math.Round(100.0 * dealt / party) : 0;
                foreach (bool aboveCharacter in places)
                {
                    Rect2? anchor = aboveCharacter ? CreatureHitboxRect(_players[s].Creature) : PartyListRect(_players[s]);
                    if (anchor == null) continue;
                    while (_partyCells.Count <= cell)
                    {
                        var panel = UiTheme.MakePanel(0.82f, 1f);
                        var label = UiTheme.MakeLabel(14, UiTheme.Cream);
                        if (GameAssets.Enabled && GameAssets.NumericFont is Font f) label.AddThemeFontOverride("font", f);
                        _partyLayer.AddChild(panel); _partyLayer.AddChild(label);
                        _partyCells.Add((panel, label));
                    }
                    var (p, l) = _partyCells[cell++];
                    l.Text = $"⚔ {dealt}  ·  {pct}%";
                    l.AddThemeFontSizeOverride("font_size", Math.Max(9, (int)Math.Round(15 * sc)));
                    l.AddThemeColorOverride("font_color", Palette[Math.Min(s, Palette.Length - 1)]);
                    var size = new Vector2(l.GetMinimumSize().X + 18f * sc, 28f * sc);
                    var r = anchor.Value;
                    // Above the character: centered over the head (under it sit the game's HP bar and effect
                    // icons). List: right after the entry's health bar, centered on the entry.
                    var pos = aboveCharacter
                        ? new Vector2(r.Position.X + (r.Size.X - size.X) / 2f, r.Position.Y - size.Y - 10f * sc)
                        : new Vector2(r.End.X + 10f * sc, r.Position.Y + (r.Size.Y - size.Y) / 2f);
                    p.Visible = l.Visible = true;
                    p.Position = pos; p.Size = size;
                    l.Position = pos + new Vector2(9f * sc, 3f * sc);
                }
            }
            for (int c = cell; c < _partyCells.Count; c++) _partyCells[c].Panel.Visible = _partyCells[c].Label.Visible = false;
        }
        catch { HidePartyHud(); }
    }

    // The player's entry in the game's top-left party list (NMultiplayerPlayerState), as a viewport rect.
    private static Rect2? PartyListRect(Player player)
    {
        if (_frame - _playerStateLookupFrame >= 60 || _playerStateNodes.Exists(n => !GodotObject.IsInstanceValid(n)))
        {
            _playerStateLookupFrame = _frame;
            _playerStateNodes.Clear();
            _playerStateType ??= AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState");
            _playerStatePlayer ??= new PropertyInfoCache(_playerStateType != null ? AccessTools.Property(_playerStateType, "Player") : null);
            if (_playerStateType != null) Collect(((SceneTree)Engine.GetMainLoop()).Root, _playerStateType, _playerStateNodes);
        }
        foreach (var node in _playerStateNodes)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsVisibleInTree()) continue;
            if (!ReferenceEquals(_playerStatePlayer?.Prop?.GetValue(node), player)) continue;
            // The entry's node is much wider than what it draws; its health bar ends where the entry ends.
            _playerStateHealthBar ??= _playerStateType != null ? AccessTools.Field(_playerStateType, "_healthBar") : null;
            if (_playerStateHealthBar?.GetValue(node) is Control bar && bar.IsVisibleInTree())
            {
                // NHealthBar is wide too; its HpBarContainer is the bar actually drawn.
                var visible = AccessTools.Property(bar.GetType(), "HpBarContainer")?.GetValue(bar) as Control ?? bar;
                var b = visible.GetGlobalRect();
                var n = node.GetGlobalRect();
                // The name row can run past the bar (status badges after the name): end after both.
                float right = b.End.X;
                _playerStateTop ??= _playerStateType != null ? AccessTools.Field(_playerStateType, "_topContainer") : null;
                if (_playerStateTop?.GetValue(node) is Control top && top.IsVisibleInTree())
                    right = Math.Max(right, top.GetGlobalRect().Position.X + top.GetCombinedMinimumSize().X * top.GetGlobalTransformWithCanvas().Scale.X);
                return new Rect2(n.Position.X, n.Position.Y, right - n.Position.X, b.End.Y - n.Position.Y);
            }
            return node.GetGlobalRect();
        }
        return null;
    }

    private static void Collect(Node node, Type type, List<Control> into)
    {
        foreach (var child in node.GetChildren())
        {
            if (type.IsInstanceOfType(child) && child is Control c) into.Add(c);
            Collect(child, type, into);
        }
    }

    // Any creature's hitbox rect (same lookup as LocalPlayerHitboxRect, cached per creature).
    private static readonly Dictionary<Creature, Control> _hitboxes = new();

    private static Rect2? CreatureHitboxRect(Creature? creature)
    {
        try
        {
            if (creature == null) return null;
            if (!_hitboxes.TryGetValue(creature, out var hb) || !GodotObject.IsInstanceValid(hb))
            {
                _hitboxes.Remove(creature);
                if (!_anchorResolved) LocalPlayerHitboxRect(); // resolves the reflection handles
                var inst = _roomInstanceProp?.GetValue(null);
                if (inst == null || _getCreatureNode == null || _hitboxProp == null) return null;
                var creatureNode = _getCreatureNode.Invoke(inst, new object[] { creature });
                if (creatureNode == null || _hitboxProp.GetValue(creatureNode) is not Control found) return null;
                _hitboxes[creature] = hb = found;
            }
            var xf = hb.GetGlobalTransformWithCanvas();
            return new Rect2(xf.Origin, hb.Size * xf.Scale);
        }
        catch { return null; }
    }
}
