using System;
using Godot;

namespace SpireLedger;

// A short message centered near the top of the screen (e.g. "Forecast: off"), drawn on its own
// CanvasLayer like the other views. One at a time; a new message replaces the current one.
internal static class Toast
{
    private static CanvasLayer? _layer;
    private static Label? _label;
    private static Panel? _panel;
    private static string? _text;
    private static double _until;

    public static void Show(string text, double seconds = 2.5)
    {
        _text = text;
        _until = Time.GetTicksMsec() / 1000.0 + seconds;
    }

    // Called every frame by the mod's Tick.
    public static void Tick()
    {
        try
        {
            bool show = _text != null && Time.GetTicksMsec() / 1000.0 < _until;
            if (!show) { if (_layer != null) _layer.Visible = false; return; }
            if (_layer == null)
            {
                _layer = new CanvasLayer { Layer = 128, Name = "SpireLedgerToast" };
                ((SceneTree)Engine.GetMainLoop()).Root.AddChild(_layer);
                _panel = UiTheme.MakePanel(0.95f, 1f);
                _layer.AddChild(_panel);
                _label = UiTheme.MakeLabel(16, UiTheme.Gold);
                if (GameAssets.Enabled && GameAssets.KreonBold is Font f) _label.AddThemeFontOverride("font", f);
                _layer.AddChild(_label);
            }
            _layer.Visible = true;
            var vp = _layer.GetViewport().GetVisibleRect().Size;
            float s = UiTheme.Scale(vp);
            _label!.Text = _text;
            _label.AddThemeFontSizeOverride("font_size", Math.Max(10, (int)Math.Round(17 * s)));
            var size = new Vector2(_label.GetMinimumSize().X + 40f * s, 44f * s);
            var pos = new Vector2((vp.X - size.X) / 2f, 90f * s);
            _panel!.Position = pos; _panel.Size = size;
            _label.Position = pos + new Vector2(20f * s, 9f * s);
        }
        catch { }
    }
}
