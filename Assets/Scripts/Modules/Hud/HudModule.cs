using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.Modules
{
    /// <summary>
    /// The debug readout: view coordinates on top, render engine and progress underneath. First
    /// module, and the shape every later one follows - it reads the session and the render status
    /// through the context, and owns nothing but its own UI objects.
    /// </summary>
    public sealed class HudModule : IAppModule
    {
        private const float UpdateInterval = 0.1f;

        /// <summary>Readout type size in dp, before the inspector's percentage.</summary>
        private const float FontSizeDp = 12f;

        private readonly float fontPercent;

        private Text scaleValueText;
        private Text computeBackendText;
        private AppServices context;
        private float nextUpdateTime;

        /// <param name="fontPercent">
        /// Inspector size, as a percentage of the density-derived default. It used to be a raw
        /// pixel count, and the value tuned into the scene (110) was compensating for a
        /// CanvasScaler that shrank the whole interface - reading it as a percentage keeps that
        /// tuned number meaningful (110 = a tenth larger) now that the scaler is fixed.
        /// </param>
        public HudModule(Text scaleValueText, Text computeBackendText, int fontPercent)
        {
            this.scaleValueText = scaleValueText;
            this.computeBackendText = computeBackendText;
            this.fontPercent = Mathf.Clamp(fontPercent, 30, 400) / 100f;
        }

        /// <summary>Type size in device pixels, from the screen's density like everything else.</summary>
        private int FontSize => Mathf.Max(8, Mathf.RoundToInt(ScreenScale.Dp(FontSizeDp) * fontPercent));

        public string Id => "hud";

        public void Initialize(AppServices services)
        {
            context = services;

            if (scaleValueText == null)
            {
                scaleValueText = CreateText("Scale", ScalePosition);
            }

            if (computeBackendText == null)
            {
                computeBackendText = CreateText("Backend", BackendPosition);
            }

            Configure(scaleValueText, ScalePosition);
            Configure(computeBackendText, BackendPosition);
            nextUpdateTime = 0f;
        }

        public void Tick()
        {
            if (context == null || Time.unscaledTime < nextUpdateTime)
            {
                return;
            }

            nextUpdateTime = Time.unscaledTime + UpdateInterval;

            var view = context.Session.View;
            var status = context.Render.Status;

            if (scaleValueText != null)
            {
                var scale = view.scale.AsDouble;
                var reference = context.Session.Definition.DefaultView.scale.AsDouble;
                var zoom = scale > 0d ? reference / scale : 0d;
                var rotationDegrees = view.rotation * (180d / Math.PI);
                rotationDegrees -= Math.Floor(rotationDegrees / 360d) * 360d;

                // Full-precision centre so a view can be reproduced on the PC via SetView().
                scaleValueText.text = string.Concat(
                    "scale  ", scale.ToString("0.000000e+00", CultureInfo.InvariantCulture), "\n",
                    "zoom   x", zoom.ToString("0.###e+00", CultureInfo.InvariantCulture), "\n",
                    "rot    ", rotationDegrees.ToString("0.0", CultureInfo.InvariantCulture), " deg\n",
                    "X  ", view.x.AsDecimal.ToString("G29", CultureInfo.InvariantCulture), "\n",
                    "Y  ", view.y.AsDecimal.ToString("G29", CultureInfo.InvariantCulture));
            }

            if (computeBackendText == null)
            {
                return;
            }

            string engineLine;
            string detailLine;
            if (status.Backend == RenderBackend.GpuFloat)
            {
                engineLine = "GPU fp32" + (status.Interacting ? "  interactive" : string.Empty);
                detailLine = "iter " + status.Iterations;
            }
            else
            {
                var precision = status.ExtendedPrecision ? "double-double" : "fp64";
                engineLine = "CPU Parallel  " + precision + (status.Interacting ? "  interactive" : string.Empty);
                detailLine = status.IsBusy
                    ? string.Concat(
                        "iter ", status.Iterations.ToString(CultureInfo.InvariantCulture),
                        "  pass ", status.Pass.ToString(CultureInfo.InvariantCulture),
                        "/", status.PassCount.ToString(CultureInfo.InvariantCulture),
                        "  ", Mathf.RoundToInt(status.Progress * 100f).ToString(CultureInfo.InvariantCulture), "%")
                    : "iter " + status.Iterations + "  done";
            }

            // Screen metrics on the readout: the interface scale is derived from a density that
            // Android devices are free to misreport, and the only way to tell a formula bug
            // from a lying panel is to see the numbers the device actually gave.
            var densityLine = string.Concat(
                "screen ", Screen.width.ToString(CultureInfo.InvariantCulture),
                "x", Screen.height.ToString(CultureInfo.InvariantCulture),
                "  dpi ", Screen.dpi.ToString("0", CultureInfo.InvariantCulture),
                "  dens ", ScreenScale.Density.ToString("0.00", CultureInfo.InvariantCulture),
                "  canvas x", CanvasScaleFactor().ToString("0.00", CultureInfo.InvariantCulture));

            computeBackendText.text = engineLine + "\n" + detailLine + "\n" + densityLine;
        }

        public void Shutdown()
        {
            context = null;
        }

        /// <summary>
        /// What the canvas multiplies our pixel sizes by. It must be 1: every size in the UI is
        /// already in device pixels. A value other than 1 means a CanvasScaler is fighting the
        /// layout, which is exactly the bug that made the interface unusable on the phone.
        /// </summary>
        private float CanvasScaleFactor()
        {
            if (context == null || context.UiRoot == null)
            {
                return 1f;
            }

            var canvas = context.UiRoot.GetComponentInParent<Canvas>();
            return canvas != null ? canvas.scaleFactor : 1f;
        }

        private Vector2 ScalePosition => new(ScreenScale.Dp(10f), -ScreenScale.Dp(10f));

        private Vector2 BackendPosition => new(ScreenScale.Dp(10f), -ScreenScale.Dp(10f) - 5.6f * FontSize);

        private Text CreateText(string objectName, Vector2 anchoredPosition)
        {
            var child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            child.transform.SetParent(context.UiRoot, false);
            var text = child.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Configure(text, anchoredPosition);
            return text;
        }

        private void Configure(Text text, Vector2 anchoredPosition)
        {
            if (text == null)
            {
                return;
            }

            // The scene's Text objects point at the old built-in Arial (removed in
            // Unity 6), so they render nothing. Force a font that ships at runtime.
            if (text.font == null || text.font.name != "LegacyRuntime")
            {
                var runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (runtimeFont != null)
                {
                    text.font = runtimeFont;
                }
            }

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(Screen.width * 2f, 8f * FontSize);
            text.alignment = TextAnchor.UpperLeft;
            text.fontSize = FontSize;
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;

            // High-contrast edge so the readout stays legible over any fractal colour.
            var outline = text.GetComponent<Outline>();
            if (outline == null)
            {
                outline = text.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = Color.black;
            var edge = Mathf.Max(1f, FontSize * 0.04f);
            outline.effectDistance = new Vector2(edge, -edge);
        }
    }
}
