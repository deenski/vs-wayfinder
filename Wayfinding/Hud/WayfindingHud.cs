using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Wayfinding.Infra;
using Wayfinding.Pathing.Map;

namespace Wayfinding;

/// <summary>
/// One line under the vanilla minimap. The text is the map layer's distance
/// and the current <see cref="NavTargets"/> label. Nothing is drawn when the
/// map is not allowed.
/// shortcut: the line is anchored to the vanilla minimap dialog, so it is not
/// drawn when that dialog is not the open HUD.
/// </summary>
public sealed class WayfindingHud : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly WayfindingModSystem mod;
    private readonly CairoFont font = CairoFont.WhiteDetailText();
    private readonly CairoFont greyFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.75, 0.75, 0.75, 1 });
    private LoadedTexture? texture;
    private string? uploadedText;
    private bool uploadedGrey;
    private int uploadedMeters = -1;
    private long uploadedAtMs;
    private bool disabled;

    public WayfindingHud(ICoreClientAPI capi, WayfindingModSystem mod)
    {
        this.capi = capi;
        this.mod = mod;
    }

    public double RenderOrder => 0.95;

    public int RenderRange => 1;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disabled || stage != EnumRenderStage.Ortho || capi.HideGuis)
        {
            return;
        }

        try
        {
            Draw();
        }
        catch (Exception ex)
        {
            disabled = true;
            texture?.Dispose();
            texture = null;
            mod.Log.LogOnce("hud", LogLevel.Error, "HUD disabled after " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    public void Dispose()
    {
        texture?.Dispose();
        texture = null;
    }

    private void Draw()
    {
        var targets = mod.NavTargets;
        if (mod.MapLayer == null || !mod.MapLayer.TryHudLine(out var line, out var meters, out var grey))
        {
            return;
        }

        if (!HudLine.Show(targets is { MapAllowed: true }, resolved: true))
        {
            return;
        }

        var dialog = capi.ModLoader.GetModSystem<WorldMapManager>().worldMapDlg;
        if (dialog == null || dialog.DialogType != EnumDialogType.HUD || !dialog.IsOpened())
        {
            return;
        }

        var bounds = dialog.SingleComposer?.Bounds;
        if (bounds == null)
        {
            return;
        }

        var now = capi.World.ElapsedMilliseconds;
        var due = LabelUpload.ShouldUpload(meters, uploadedMeters, now, uploadedAtMs);
        if (due || ((line != uploadedText || grey != uploadedGrey) && meters == uploadedMeters))
        {
            capi.Gui.TextTexture.GenOrUpdateTextTexture(line, grey ? greyFont : font, ref texture);
            uploadedText = line;
            uploadedGrey = grey;
            uploadedMeters = meters;
            uploadedAtMs = now;
        }

        if (texture == null || texture.TextureId == 0)
        {
            return;
        }

        var x = (float)bounds.renderX;
        var y = (float)(bounds.renderY + bounds.OuterHeight);
        capi.Render.Render2DLoadedTexture(texture, x, y);
    }
}
